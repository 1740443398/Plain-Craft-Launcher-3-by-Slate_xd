Imports System
Imports System.Collections.Generic
Imports System.Linq

Module TestRunner
    Dim Passed As Integer = 0
    Dim Failed As Integer = 0

    Sub Check(name As String, actual As Object, expected As Object)
        If actual.ToString() = expected.ToString() Then
            Passed += 1
        Else
            Failed += 1
            Console.WriteLine("  FAIL  " & name & "  got=" & actual.ToString() & "  want=" & expected.ToString())
        End If
    End Sub

    Sub Main()
        Console.WriteLine("=== 基础比较 ===")
        Check("1.20.1 = 1.20.1", ModVersionRange.MatchVersion("1.20.1", "1.20.1"), VersionMatch.Satisfied)
        Check("1.20.1 = 1.20.2", ModVersionRange.MatchVersion("1.20.1", "1.20.2"), VersionMatch.Unsatisfied)
        Check("1.20.1 >= 1.20", ModVersionRange.MatchVersion("1.20.1", ">=1.20"), VersionMatch.Satisfied)
        Check("1.19.4 >= 1.20", ModVersionRange.MatchVersion("1.19.4", ">=1.20"), VersionMatch.Unsatisfied)
        Check("1.20.1 < 1.21", ModVersionRange.MatchVersion("1.20.1", "<1.21"), VersionMatch.Satisfied)
        Check("1.20.1 <= 1.20.1", ModVersionRange.MatchVersion("1.20.1", "<=1.20.1"), VersionMatch.Satisfied)
        Check("通配 *", ModVersionRange.MatchVersion("1.20.1", "*"), VersionMatch.Satisfied)
        Check("空约束", ModVersionRange.MatchVersion("1.20.1", ""), VersionMatch.Satisfied)

        Console.WriteLine("=== 多段约束（与）===")
        Check(">=1.20 <1.21 满足", ModVersionRange.MatchVersion("1.20.4", ">=1.20 <1.21"), VersionMatch.Satisfied)
        Check(">=1.20 <1.21 不满足", ModVersionRange.MatchVersion("1.21.0", ">=1.20 <1.21"), VersionMatch.Unsatisfied)

        Console.WriteLine("=== 或 || ===")
        Check("1.19 或 1.20", ModVersionRange.MatchVersion("1.19.2", "1.19.2||1.20.1"), VersionMatch.Satisfied)
        Check("都不匹配", ModVersionRange.MatchVersion("1.18.2", "1.19.2||1.20.1"), VersionMatch.Unsatisfied)

        Console.WriteLine("=== Fabric 插入符 ^ ===")
        Check("^1.20 配 1.20.4", ModVersionRange.MatchVersion("1.20.4", "^1.20"), VersionMatch.Satisfied)
        Check("^1.20 配 2.0", ModVersionRange.MatchVersion("2.0.0", "^1.20"), VersionMatch.Unsatisfied)
        Check("^0.2.1 配 0.9.0", ModVersionRange.MatchVersion("0.9.0", "^0.2.1"), VersionMatch.Satisfied)
        Check("^0.2.1 配 1.0.0", ModVersionRange.MatchVersion("1.0.0", "^0.2.1"), VersionMatch.Unsatisfied)

        Console.WriteLine("=== Fabric 波浪号 ~ ===")
        Check("~1.20 配 1.20.4", ModVersionRange.MatchVersion("1.20.4", "~1.20"), VersionMatch.Satisfied)
        Check("~1.20 配 1.21", ModVersionRange.MatchVersion("1.21.0", "~1.20"), VersionMatch.Unsatisfied)

        Console.WriteLine("=== 通配 x ===")
        Check("1.20.x 配 1.20.4", ModVersionRange.MatchVersion("1.20.4", "1.20.x"), VersionMatch.Satisfied)
        Check("1.20.x 配 1.21.0", ModVersionRange.MatchVersion("1.21.0", "1.20.x"), VersionMatch.Unsatisfied)
        Check("1.* 配 1.5.2", ModVersionRange.MatchVersion("1.5.2", "1.*"), VersionMatch.Satisfied)

        Console.WriteLine("=== 预发布 ===")
        Check("1.20-beta < 1.20", ModVersionRange.MatchVersion("1.20-beta", "<1.20"), VersionMatch.Satisfied)
        Check("1.20 > 1.20-beta", ModVersionRange.MatchVersion("1.20", ">1.20-beta"), VersionMatch.Satisfied)
        Check("1.20-beta.2 > 1.20-beta.1", ModVersionRange.MatchVersion("1.20-beta.2", ">1.20-beta.1"), VersionMatch.Satisfied)

        Console.WriteLine("=== Maven 区间 ===")
        Check("[1.0,2.0) 配 1.5", ModVersionRange.MatchVersion("1.5", "[1.0,2.0)", "maven"), VersionMatch.Satisfied)
        Check("[1.0,2.0) 配 2.0", ModVersionRange.MatchVersion("2.0", "[1.0,2.0)", "maven"), VersionMatch.Unsatisfied)
        Check("[1.0,2.0) 配 1.0", ModVersionRange.MatchVersion("1.0", "[1.0,2.0)", "maven"), VersionMatch.Satisfied)
        Check("[1.20] 配 1.20", ModVersionRange.MatchVersion("1.20", "[1.20]", "maven"), VersionMatch.Satisfied)
        Check("[1.20] 配 1.21", ModVersionRange.MatchVersion("1.21", "[1.20]", "maven"), VersionMatch.Unsatisfied)
        Check("裸版本号 maven", ModVersionRange.MatchVersion("1.5", "1.5", "maven"), VersionMatch.Unknown)

        Console.WriteLine("=== Unknown 保护（关键）===")
        Check("未知版本标记", ModVersionRange.MatchVersion("未知版本", ">=1.0"), VersionMatch.Unknown)
        Check("unknown 字符串", ModVersionRange.MatchVersion("unknown", ">=1.0"), VersionMatch.Unknown)
        Check("空实际版本", ModVersionRange.MatchVersion("", ">=1.0"), VersionMatch.Unknown)
        Check("占位符", ModVersionRange.MatchVersion("${version}", ">=1.0"), VersionMatch.Unknown)
        Check("latest", ModVersionRange.MatchVersion("1.5", "latest"), VersionMatch.Unknown)
        Check("畸形约束", ModVersionRange.MatchVersion("1.5", ">=<<1.0"), VersionMatch.Unknown)

        Console.WriteLine()
        Console.WriteLine("通过 " & Passed & "，失败 " & Failed)
        If Failed > 0 Then Environment.ExitCode = 1
    End Sub
End Module