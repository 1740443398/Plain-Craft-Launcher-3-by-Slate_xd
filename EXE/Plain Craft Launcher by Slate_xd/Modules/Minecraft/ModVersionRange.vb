Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.RegularExpressions

''' <summary>
''' 模组依赖的版本范围比较（三态）。
''' <para>
''' 返回三种结果，而不是简单的 True/False：
''' <c>Satisfied</c> 确定满足、<c>Unsatisfied</c> 确定不满足、<c>Unknown</c> 无法确认。
''' </para>
''' <para>
''' Unknown 是刻意设计，**绝不能**被当成启动错误。版本写法千奇百怪，
''' 猜错会让玩家无法启动本来正常的游戏；宁可标为未知，交给玩家自己判断。
''' </para>
''' <para>
''' 算法参考 PCL-Living-Fork 的 ModDependencyVersions（LF LICENCE），
''' 按本项目 VB.NET 风格重写，未引入其策略层。
''' </para>
''' </summary>
Friend Module ModVersionRange

    ''' <summary>版本范围比较结果。</summary>
    Friend Enum VersionMatch
        ''' <summary>无法确认，调用方不得据此报错。</summary>
        Unknown = 0
        ''' <summary>确定满足约束。</summary>
        Satisfied = 1
        ''' <summary>确定不满足约束。</summary>
        Unsatisfied = 2
    End Enum

    ''' <summary>
    ''' 合并多个比较结果。<paramref name="AllTrue"/> 为 True 时按「全部满足」合并，否则按「任一满足」合并。
    ''' </summary>
    Friend Function Combine(states As IEnumerable(Of VersionMatch), AllTrue As Boolean) As VersionMatch
        Dim Values = states.ToArray()
        If Values.Length = 0 Then Return VersionMatch.Unknown
        If AllTrue Then
            If Values.Contains(VersionMatch.Unsatisfied) Then Return VersionMatch.Unsatisfied
            If Values.Contains(VersionMatch.Unknown) Then Return VersionMatch.Unknown
            Return VersionMatch.Satisfied
        Else
            If Values.Contains(VersionMatch.Satisfied) Then Return VersionMatch.Satisfied
            If Values.Contains(VersionMatch.Unknown) Then Return VersionMatch.Unknown
            Return VersionMatch.Unsatisfied
        End If
    End Function

    ''' <summary>
    ''' 比较单个版本号与一个范围表达式。
    ''' </summary>
    ''' <param name="Actual">实际版本，例如 "1.20.1"。</param>
    ''' <param name="Requirement">约束表达式，例如 ">=1.20 &lt;1.21"、"^0.2.1"、"[1.0,2.0)"。</param>
    ''' <param name="Syntax">"fabric"（默认）或 "maven"。</param>
    Friend Function MatchVersion(Actual As String, Requirement As String, Optional Syntax As String = "fabric") As VersionMatch
        Return MatchRange(Actual, Requirement, Syntax)
    End Function

    ''' <summary>内部的递归实现，带深度上限防止恶意嵌套。</summary>
    Private Function MatchRange(Actual As String, Range As String, Syntax As String, Optional Depth As Integer = 0) As VersionMatch
        If Depth > 8 Then Return VersionMatch.Unknown
        If Range Is Nothing Then Return VersionMatch.Unknown

        Range = Range.Trim()
        If Range.Length = 0 OrElse Range = "*" Then Return VersionMatch.Satisfied

        '实际或约束里出现占位符、未知标记时，一律不给结论
        If String.IsNullOrWhiteSpace(Actual) OrElse Actual = "未知版本" OrElse
           Actual.Equals("unknown", StringComparison.OrdinalIgnoreCase) OrElse
           Actual.Contains("${") OrElse Range.Contains("${") Then
            Return VersionMatch.Unknown
        End If

        If Syntax = "maven" Then Return MatchMaven(Actual, Range)

        ' || 表示「或」，逐段比较后按「任一满足」合并
        If Range.Contains("||") Then
            Dim Segments = Range.Split(New String() {"||"}, StringSplitOptions.None)
            Return Combine(Segments.Select(Function(s) MatchRange(Actual, s, Syntax, Depth + 1)), False)
        End If

        '把 ">= 1.20" 这种带空格的运算符收紧成 ">=1.20"
        Range = Regex.Replace(Range, "([><=~^]+)\s+", "$1")
        Dim Tokens = Regex.Split(Range, "\s+").Where(Function(s) Not String.IsNullOrEmpty(s)).ToArray()
        If Tokens.Length = 0 Then Return VersionMatch.Unknown

        '任何一段不符合版本表达式形状就判未知，不做模糊猜测
        For Each Token In Tokens
            If Not Regex.IsMatch(Token, "^(?:>=|<=|>|<|=|~|\^)?(?:[A-Za-z0-9][A-Za-z0-9._:+*-]*|\*)$") Then
                Return VersionMatch.Unknown
            End If
        Next

        '多个约束默认是「与」的关系
        Return Combine(Tokens.Select(Function(t) MatchPredicate(Actual, t, Syntax)), True)
    End Function

    ''' <summary>解析出的数字版本：主段数字 + 可选的预发布标记。</summary>
    Private Class NumericVersion
        Friend Parts As Long()
        ''' <summary>预发布标记；Nothing 表示正式版，"..." 表示有预发布。</summary>
        Friend Prerelease As String
    End Class

    ''' <summary>
    ''' 把 "1.20.1-beta.2+build" 拆成数字段与预发布段。解析失败返回 Nothing。
    ''' </summary>
    Private Function ParseVersion(Value As String) As NumericVersion
        If Value Is Nothing Then Return Nothing
        Dim M = Regex.Match(Value, "^(\d+(?:\.\d+)*)(?:-((?:[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?))?(?:\+[0-9A-Za-z.-]+)?$")
        If Not M.Success Then Return Nothing
        Dim Parts As New List(Of Long)
        For Each Part In M.Groups(1).Value.Split("."c)
            Dim Number As Long
            If Not Long.TryParse(Part, Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, Number) Then Return Nothing
            Parts.Add(Number)
        Next
        Dim Pre As String = Nothing
        'Fabric 的 "1.21-" 空预发布键与正式版 1.21 不同，保留为预发布下界
        If M.Groups(2).Success Then Pre = M.Groups(2).Value
        Return New NumericVersion With {.Parts = Parts.ToArray(), .Prerelease = Pre}
    End Function

    ''' <summary>
    ''' 比较两个解析后的版本。返回 &lt;0、0、&gt;0。
    ''' <para>正式版大于同号预发布；预发布之间按段比较，数字段优先于字母段。</para>
    ''' </summary>
    Private Function CompareVersions(Left As NumericVersion, Right As NumericVersion) As Integer
        Dim Max = Math.Max(Left.Parts.Length, Right.Parts.Length)
        For Index = 0 To Max - 1
            Dim L As Long = If(Index < Left.Parts.Length, Left.Parts(Index), 0L)
            Dim R As Long = If(Index < Right.Parts.Length, Right.Parts(Index), 0L)
            Dim C = L.CompareTo(R)
            If C <> 0 Then Return C
        Next
        If String.Equals(Left.Prerelease, Right.Prerelease, StringComparison.Ordinal) Then Return 0
        If Left.Prerelease Is Nothing Then Return 1
        If Right.Prerelease Is Nothing Then Return -1
        If Left.Prerelease.Length = 0 Then Return -1
        If Right.Prerelease.Length = 0 Then Return 1
        Dim LP = Left.Prerelease.Split("."c)
        Dim RP = Right.Prerelease.Split("."c)
        For Index = 0 To Math.Min(LP.Length, RP.Length) - 1
            Dim LN As Long, RN As Long
            Dim LNum = Long.TryParse(LP(Index), LN)
            Dim RNum = Long.TryParse(RP(Index), RN)
            Dim C As Integer
            If LNum AndAlso RNum Then
                C = LN.CompareTo(RN)
            ElseIf LNum <> RNum Then
                C = If(LNum, -1, 1)
            Else
                C = StringComparer.Ordinal.Compare(LP(Index), RP(Index))
            End If
            If C <> 0 Then Return C
        Next
        Return LP.Length.CompareTo(RP.Length)
    End Function

    ''' <summary>匹配单个谓词，例如 "&gt;=1.20"、"^0.2.1"、"1.20.x"。</summary>
    Private Function MatchPredicate(Actual As String, Predicate As String, Syntax As String) As VersionMatch
        If Predicate = "*" OrElse Predicate = "x" OrElse Predicate = "X" Then Return VersionMatch.Satisfied

        Dim M = Regex.Match(Predicate, "^(>=|<=|>|<|=|~|\^)?(.+)$")
        If Not M.Success Then Return VersionMatch.Unknown
        Dim OperatorText = M.Groups(1).Value
        Dim Expected = M.Groups(2).Value
        Dim Current = ParseVersion(Actual)

        '通配写法：1.20.x / 1.20.*
        If Regex.IsMatch(Expected, "^(?:\d+\.)*(?:\*|[xX])$") Then
            If OperatorText.Length > 0 AndAlso OperatorText <> "=" Then Return VersionMatch.Unknown
            If Current Is Nothing Then Return VersionMatch.Unknown
            Dim Prefix = Expected.Split("."c).TakeWhile(Function(p) p <> "*" AndAlso p <> "x" AndAlso p <> "X").ToArray()
            For Index = 0 To Prefix.Length - 1
                Dim Number As Long
                If Not Long.TryParse(Prefix(Index), Number) Then Return VersionMatch.Unknown
                Dim Cur As Long = If(Index < Current.Parts.Length, Current.Parts(Index), 0L)
                If Cur <> Number Then Return VersionMatch.Unsatisfied
            Next
            Return VersionMatch.Satisfied
        End If

        Dim Target = ParseVersion(Expected)
        If Current Is Nothing OrElse Target Is Nothing Then
            '双方都不是标准数字版本时，只做原样相等比较，且仅限像版本号的字符串
            If OperatorText.Length > 0 AndAlso OperatorText <> "=" Then Return VersionMatch.Unknown
            If Not Regex.IsMatch(Expected, "^[A-Za-z0-9][A-Za-z0-9._:+-]*$") OrElse Expected.EndsWith("+") Then Return VersionMatch.Unknown
            Dim Lower = Expected.ToLowerInvariant()
            If Lower = "latest" OrElse Lower = "latest.release" OrElse Lower = "latest.integration" Then Return VersionMatch.Unknown
            Return If(String.Equals(Actual, Expected, StringComparison.Ordinal), VersionMatch.Satisfied, VersionMatch.Unsatisfied)
        End If

        Dim Comparison = CompareVersions(Current, Target)
        Select Case OperatorText
            Case "", "="
                Return If(Comparison = 0, VersionMatch.Satisfied, VersionMatch.Unsatisfied)
            Case ">"
                Return If(Comparison > 0, VersionMatch.Satisfied, VersionMatch.Unsatisfied)
            Case ">="
                Return If(Comparison >= 0, VersionMatch.Satisfied, VersionMatch.Unsatisfied)
            Case "<"
                Return If(Comparison < 0, VersionMatch.Satisfied, VersionMatch.Unsatisfied)
            Case "<="
                Return If(Comparison <= 0, VersionMatch.Satisfied, VersionMatch.Unsatisfied)
            Case "~", "^"
                If Syntax = "fabric" Then
                    'Fabric：^ 要求主版本相同，~ 要求主、次版本相同（主版本为 0 时也一样）
                    Dim Last = If(OperatorText = "~", 1, 0)
                    For Index = 0 To Last
                        Dim Cur As Long = If(Index < Current.Parts.Length, Current.Parts(Index), 0L)
                        Dim Tgt As Long = If(Index < Target.Parts.Length, Target.Parts(Index), 0L)
                        If Cur <> Tgt Then Return VersionMatch.Unsatisfied
                    Next
                    Return If(Comparison >= 0, VersionMatch.Satisfied, VersionMatch.Unsatisfied)
                End If
                'Maven：算出上界，当前版本需落在 [目标, 上界) 内
                Dim Upper = CType(Target.Parts.Clone(), Long())
                Dim Bump As Integer
                If OperatorText = "~" Then
                    Bump = If(Upper.Length > 1, 1, 0)
                Else
                    Bump = 0
                    While Bump < Upper.Length - 1 AndAlso Upper(Bump) = 0L
                        Bump += 1
                    End While
                End If
                If Upper(Bump) = Long.MaxValue Then Return VersionMatch.Unknown
                Upper(Bump) += 1L
                For Index = Bump + 1 To Upper.Length - 1
                    Upper(Index) = 0L
                Next
                Dim UpperVersion As New NumericVersion With {.Parts = Upper, .Prerelease = Nothing}
                Return If(Comparison >= 0 AndAlso CompareVersions(Current, UpperVersion) < 0, VersionMatch.Satisfied, VersionMatch.Unsatisfied)
            Case Else
                Return VersionMatch.Unknown
        End Select
    End Function

    ''' <summary>Maven 区间写法，例如 "[1.0,2.0)"、"[1.20]"。</summary>
    Private Function MatchMaven(Actual As String, Range As String) As VersionMatch
        '裸版本号在 Maven 里只是偏好，不是硬约束
        If Not Range.StartsWith("[") AndAlso Not Range.StartsWith("(") Then Return VersionMatch.Unknown

        Dim Intervals = Regex.Matches(Range, "[\[\(][^\[\]\(\)]*[\]\)]")
        If Intervals.Count = 0 Then Return VersionMatch.Unknown
        If Regex.Replace(Range, "[\[\(][^\[\]\(\)]*[\]\)]", "").Trim(" "c, ","c).Length > 0 Then Return VersionMatch.Unknown

        Dim Values As New List(Of VersionMatch)
        Dim Current = ParseVersion(Actual)

        For Each Interval As Match In Intervals
            Dim Raw = Interval.Value
            Dim Body = Raw.Substring(1, Raw.Length - 2)
            Dim Parts = Body.Split(","c)

            If Parts.Length = 1 Then
                '单点：只有 [x] 才是精确匹配
                If Raw(0) <> "["c OrElse Raw(Raw.Length - 1) <> "]"c Then
                    Values.Add(VersionMatch.Unknown)
                Else
                    Dim Bound = Parts(0).Trim()
                    Dim Target = ParseVersion(Bound)
                    If Current Is Nothing OrElse Target Is Nothing OrElse
                       Not Regex.IsMatch(Actual, "^\d+(?:\.\d+)*$") OrElse
                       Not Regex.IsMatch(Bound, "^\d+(?:\.\d+)*$") Then
                        Values.Add(If(String.Equals(Actual, Bound, StringComparison.Ordinal), VersionMatch.Satisfied, VersionMatch.Unknown))
                    Else
                        Values.Add(If(CompareVersions(Current, Target) = 0, VersionMatch.Satisfied, VersionMatch.Unsatisfied))
                    End If
                End If
                Continue For
            End If

            'Maven 的限定符排序与 SemVer 不同，区间只对纯数字版本给结论
            If Parts.Length <> 2 OrElse Current Is Nothing OrElse Not Regex.IsMatch(Actual, "^\d+(?:\.\d+)*$") Then
                Values.Add(VersionMatch.Unknown)
                Continue For
            End If

            Dim Bounds As New List(Of VersionMatch)
            For Index = 0 To 1
                Dim Bound = Parts(Index).Trim()
                If Bound.Length = 0 Then
                    Bounds.Add(VersionMatch.Satisfied)
                    Continue For
                End If
                Dim Target = ParseVersion(Bound)
                If Target Is Nothing OrElse Not Regex.IsMatch(Bound, "^\d+(?:\.\d+)*$") Then
                    Bounds.Add(VersionMatch.Unknown)
                    Continue For
                End If
                Dim Compare = CompareVersions(Current, Target)
                Dim Fits As Boolean
                If Index = 0 Then
                    Fits = Compare > 0 OrElse (Compare = 0 AndAlso Raw(0) = "["c)
                Else
                    Fits = Compare < 0 OrElse (Compare = 0 AndAlso Raw(Raw.Length - 1) = "]"c)
                End If
                Bounds.Add(If(Fits, VersionMatch.Satisfied, VersionMatch.Unsatisfied))
            Next
            Values.Add(Combine(Bounds, True))
        Next

        Return Combine(Values, False)
    End Function

End Module
