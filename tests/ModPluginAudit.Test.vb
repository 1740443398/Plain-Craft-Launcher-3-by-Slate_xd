Imports System
Imports System.IO

Module AuditTest
    Dim Passed As Integer = 0
    Dim Failed As Integer = 0

    Sub Check(name As String, cond As Boolean, detail As String)
        If cond Then
            Passed += 1
            Console.WriteLine("  OK    " & name & "  " & detail)
        Else
            Failed += 1
            Console.WriteLine("  FAIL  " & name & "  " & detail)
        End If
    End Sub

    Function MakePlugin(root As String, name As String, manifest As String, mainXaml As String) As ModPlugin.PluginEntry
        Dim d = Path.Combine(root, "PCL\Plugins", name)
        Directory.CreateDirectory(d)
        File.WriteAllText(Path.Combine(d, "plugin.json"), manifest)
        File.WriteAllText(Path.Combine(d, "main.xaml"), mainXaml)
        Return New ModPlugin.PluginEntry With {.Directory = d, .Manifest = New ModPlugin.PluginManifest With {.Id = name, .Name = name, .Version = "1.0.0"}}
    End Function

    Sub Main(args As String())
        Environment.SetEnvironmentVariable("PCL_TEST_BASE", args(0))

        Console.WriteLine("=== 1. 干净插件应通过 ===")
        Dim clean = MakePlugin(args(0), "clean", "{}", "<local:MyCard Title=""hi""><TextBlock Text=""hello"" /></local:MyCard>")
        Dim f1 = ModPluginAudit.Audit(clean)
        Check("干净插件无危险", ModPluginAudit.HighestRisk(f1) = ModPluginAudit.RiskLevel.Safe, ModPluginAudit.Summarize(f1))

        Console.WriteLine()
        Console.WriteLine("=== 2. 含 Process.Start 应被拒 ===")
        Dim evil = MakePlugin(args(0), "evil", "{}", "<TextBlock Text=""x"" Tag=""{x:Static Process.Start}"" />")
        Dim f2 = ModPluginAudit.Audit(evil)
        Check("检测到进程执行", ModPluginAudit.HighestRisk(f2) = ModPluginAudit.RiskLevel.Dangerous, ModPluginAudit.Summarize(f2))
        Dim hitRule As Boolean = False
        For Each f In f2
            If f.Rule = "进程执行" Then hitRule = True
        Next
        Check("命中规则名为进程执行", hitRule, "")

        Console.WriteLine()
        Console.WriteLine("=== 3. 本地路径读取应被拒 ===")
        Dim theft = MakePlugin(args(0), "theft", "{}", "<Image Source=""file:///C:/Windows/win.ini"" />")
        Dim f3 = ModPluginAudit.Audit(theft)
        Check("检测到本地文件读取", ModPluginAudit.HighestRisk(f3) = ModPluginAudit.RiskLevel.Dangerous, ModPluginAudit.Summarize(f3))

        Console.WriteLine()
        Console.WriteLine("=== 4. 路径穿越应被拒 ===")
        Dim trav = MakePlugin(args(0), "trav", "{}", "<Image Source=""../../secret.png"" />")
        Dim f4 = ModPluginAudit.Audit(trav)
        Check("检测到路径穿越", ModPluginAudit.HighestRisk(f4) = ModPluginAudit.RiskLevel.Dangerous, ModPluginAudit.Summarize(f4))

        Console.WriteLine()
        Console.WriteLine("=== 5. 仅网络访问应为 Caution 不拒绝 ===")
        Dim netp = MakePlugin(args(0), "netp", "{}", "<TextBlock Text=""https://example.com/api"" />")
        Dim f5 = ModPluginAudit.Audit(netp)
        Check("网络访问是 Caution", ModPluginAudit.HighestRisk(f5) = ModPluginAudit.RiskLevel.Caution, ModPluginAudit.Summarize(f5))

        Console.WriteLine()
        Console.WriteLine("=== 6. 声明 needExecute 应提示无效 ===")
        Dim ex = MakePlugin(args(0), "ex", "{""id"":""ex"",""name"":""ex"",""version"":""1""}", "<TextBlock Text=""ok"" />")
        ex.Manifest.NeedExecute = True
        Dim f6 = ModPluginAudit.Audit(ex)
        Dim hasHint As Boolean = False
        For Each f In f6
            If f.Rule = "无用的权限声明" Then hasHint = True
        Next
        Check("提示 needExecute 无效", hasHint, "")

        Console.WriteLine()
        Console.WriteLine("=== 7. 脚本里的 eval 应被拒 ===")
        Dim ev = MakePlugin(args(0), "ev", "{}", "<TextBlock Text=""a"" />")
        File.WriteAllText(Path.Combine(ev.Directory, "main.js"), "eval('alert(1)');")
        Dim f7 = ModPluginAudit.Audit(ev)
        Check("检测到动态执行", ModPluginAudit.HighestRisk(f7) = ModPluginAudit.RiskLevel.Dangerous, ModPluginAudit.Summarize(f7))

        Console.WriteLine()
        Console.WriteLine("通过 " & Passed & "，失败 " & Failed)
        If Failed > 0 Then Environment.ExitCode = 1
    End Sub
End Module