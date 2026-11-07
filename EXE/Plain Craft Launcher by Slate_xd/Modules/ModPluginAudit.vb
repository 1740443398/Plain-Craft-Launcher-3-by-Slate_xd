Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.RegularExpressions

''' <summary>
''' 插件安全审计：在加载前静态检查插件内容，拦住明显危险的东西。
''' <para>
''' <b>为什么要静态扫描</b>：XAML 和 JScript 本身已经拿不到 CLR 权限，
''' 但仍有几类东西能在「合法」范围内造成实际伤害：
''' </para>
''' <list type="bullet">
''' <item>通过 <c>Process.Start</c> 之类的 XAML 事件偷跑外部程序</item>
''' <item>把 <c>file://</c> 或本地绝对路径塞进图片、链接里读取用户文件</item>
''' <item>在脚本里拼出 <c>../</c> 试图逃出插件目录</item>
''' <item>远程加载 XAML 或脚本（等于绕过本地审计）</item>
''' </list>
''' <para>
''' <b>这套扫描不是沙箱</b>，它只做静态模式匹配，绕过方式是存在的。
''' 它的价值在于：把「不小心写错」和「明显恶意」挡在门外，
''' 并让用户在安装前看到一个基于实际内容的判断，而不是只看作者的自述。
''' </para>
''' </summary>
Public Module ModPluginAudit

    ''' <summary>风险等级。</summary>
    Public Enum RiskLevel
        ''' <summary>没发现问题。</summary>
        Safe = 0
        ''' <summary>有点可疑，但可能是正常需求。</summary>
        Caution = 1
        ''' <summary>明确危险，直接拒绝加载。</summary>
        Dangerous = 2
    End Enum

    ''' <summary>一条审计发现。</summary>
    Public Class Finding
        Public Property Level As RiskLevel = RiskLevel.Safe
        ''' <summary>命中的规则名。</summary>
        Public Property Rule As String = ""
        ''' <summary>给人看的说明。</summary>
        Public Property Message As String = ""
        ''' <summary>命中的原文片段（截断）。</summary>
        Public Property Snippet As String = ""
    End Class

    ''' <summary>
    ''' 危险模式。命中即拒绝加载。
    ''' </summary>
    Private ReadOnly DangerousPatterns As New List(Of Tuple(Of String, String, String)) From {
        Tuple.Create("进程执行", "\b(Process\.Start|Shell|WScript\.Shell|cmd\.exe|powershell|pwsh)\b",
                     "插件不能启动外部程序。这是硬限制。"),
        Tuple.Create("注册表写入", "\b(Registry|RegWrite|HKEY_)",
                     "插件不能写注册表。"),
        Tuple.Create("本地文件读取", "file://|\\\\\\\\\?\\|[A-Za-z]:\\\\",
                     "插件不能访问插件目录之外的本地路径。"),
        Tuple.Create("远程代码", "(eval\s*\(|new\s+Function\s*\(|execScript|ActiveXObject)",
                     "插件不能动态执行代码或创建 ActiveX 对象。"),
        Tuple.Create("路径穿越", "\.\./|\.\.\\\\",
                     "插件不能引用插件目录之外的路径。")
    }

    ''' <summary>
    ''' 需要注意但不拒绝的模式。
    ''' </summary>
    Private ReadOnly CautionPatterns As New List(Of Tuple(Of String, String, String)) From {
        Tuple.Create("网络访问", "https?://",
                     "插件会向外部地址发请求，确认域名可信。"),
        Tuple.Create("剪贴板", "\b(Clipboard|GetText|SetText)\b",
                     "插件可能读写剪贴板。"),
        Tuple.Create("输入模拟", "\b(SendKeys|keybd_event|mouse_event)\b",
                     "插件可能模拟键鼠输入。")
    }

    ''' <summary>
    ''' 审计一个插件目录里的全部文本资源。
    ''' </summary>
    Public Function Audit(Entry As PluginEntry) As List(Of Finding)
        Dim Out As New List(Of Finding)
        Try
            If Entry Is Nothing Then Return Out
            Dim Dir = Entry.Directory
            If String.IsNullOrWhiteSpace(Dir) OrElse Not DirectoryUtils.Exists(Dir) Then Return Out

            '只扫这几种文件，避免扫到用户自己的大文件
            Dim Exts = {".xaml", ".js", ".json", ".xml", ".txt", ".md"}
            Dim Files As New List(Of String)
            Try
                For Each F In System.IO.Directory.GetFiles(Dir, "*.*", System.IO.SearchOption.AllDirectories)
                    If Exts.Contains(System.IO.Path.GetExtension(F).ToLowerInvariant()) Then Files.Add(F)
                    '限制数量，防止有人塞几千个文件拖死扫描
                    If Files.Count >= 60 Then Exit For
                Next
            Catch ex As Exception
                Logger.Warn(ex, "枚举插件文件失败")
            End Try

            For Each F In Files
                Try
                    Dim Info As New System.IO.FileInfo(F)
                    If Info.Length > 512 * 1024 Then Continue For
                    Dim Text = IO.File.ReadAllText(F)
                    Dim Rel = F.Substring(Dir.TrimEnd("\"c).Length).TrimStart("\"c)

                    For Each P In DangerousPatterns
                        Dim M = Regex.Match(Text, P.Item2, RegexOptions.IgnoreCase)
                        If M.Success Then
                            Out.Add(New Finding With {
                                .Level = RiskLevel.Dangerous,
                                .Rule = P.Item1,
                                .Message = $"{P.Item3}（{Rel}）",
                                .Snippet = Clip(Text, M.Index)
                            })
                        End If
                    Next

                    For Each P In CautionPatterns
                        Dim M = Regex.Match(Text, P.Item2, RegexOptions.IgnoreCase)
                        If M.Success Then
                            Out.Add(New Finding With {
                                .Level = RiskLevel.Caution,
                                .Rule = P.Item1,
                                .Message = $"{P.Item3}（{Rel}）",
                                .Snippet = Clip(Text, M.Index)
                            })
                        End If
                    Next
                Catch ex As Exception
                    Logger.Warn(ex, "审计文件失败")
                End Try
            Next

            '清单声明了 needExecute 但我们根本不提供这个能力，提示作者改掉
            If Entry.Manifest IsNot Nothing AndAlso Entry.Manifest.NeedExecute Then
                Out.Add(New Finding With {
                    .Level = RiskLevel.Caution,
                    .Rule = "无用的权限声明",
                    .Message = "插件声明了 needExecute，但启动器不会给插件执行外部程序的能力，这项声明没有任何作用。",
                    .Snippet = ""
                })
            End If
        Catch ex As Exception
            Logger.Warn(ex, "插件审计失败")
        End Try
        Return Out
    End Function

    ''' <summary>取最高风险等级。</summary>
    Public Function HighestRisk(Findings As List(Of Finding)) As RiskLevel
        If Findings Is Nothing OrElse Findings.Count = 0 Then Return RiskLevel.Safe
        Return Findings.Max(Function(f) f.Level)
    End Function

    ''' <summary>一段可读的结论，给用户看。</summary>
    Public Function Summarize(Findings As List(Of Finding)) As String
        If Findings Is Nothing OrElse Findings.Count = 0 Then Return "未发现风险项"
        Dim Level = HighestRisk(Findings)
        Dim Danger = Findings.Where(Function(f) f.Level = RiskLevel.Dangerous).Count()
        Dim Caution = Findings.Where(Function(f) f.Level = RiskLevel.Caution).Count()
        Select Case Level
            Case RiskLevel.Dangerous
                Return $"发现 {Danger} 项危险内容，插件已被拒绝加载"
            Case RiskLevel.Caution
                Return $"发现 {Caution} 项需要注意的内容"
            Case Else
                Return "未发现风险项"
        End Select
    End Function

    Private Function Clip(Text As String, Index As Integer) As String
        Try
            Dim Start = Math.Max(0, Index - 20)
            Dim Len = Math.Min(80, Text.Length - Start)
            If Len <= 0 Then Return ""
            Return Text.Substring(Start, Len).Replace(vbCrLf, " ").Replace(vbLf, " ")
        Catch
            Return ""
        End Try
    End Function

End Module
