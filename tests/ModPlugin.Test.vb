Imports System
Imports System.IO
Imports System.Collections.Generic

Module PluginTest
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

    Sub Main(args As String())
        Dim baseDir = args(0)
        ' 模拟 Paths.Base
        Environment.SetEnvironmentVariable("PCL_TEST_BASE", baseDir)

        Console.WriteLine("=== 扫描插件 ===")
        ModPlugin.Scan()
        Dim all = ModPlugin.Plugins
        Console.WriteLine("  发现 " & all.Count.ToString() & " 个插件")
        Console.WriteLine("")

        For Each p In all
            Console.WriteLine("  [" & p.DisplayName & "]")
            Console.WriteLine("    目录: " & Path.GetFileName(p.Directory))
            Console.WriteLine("    可用: " & p.IsUsable.ToString())
            If Not String.IsNullOrEmpty(p.FailureReason) Then
                Console.WriteLine("    失败: " & p.FailureReason)
            End If
            If p.Manifest IsNot Nothing Then
                Console.WriteLine("    ID: " & p.Manifest.Id & "  v" & p.Manifest.Version)
                Console.WriteLine("    权限: " & If(p.Manifest.DeclaredPermissions.Count = 0, "(无)", String.Join(", ", p.Manifest.DeclaredPermissions)))
            End If
        Next
        Console.WriteLine("")

        ' 断言
        Dim good = all.Where(Function(p) p.IsUsable).ToList()
        Check("至少发现 1 个可用插件", good.Count >= 1, good.Count.ToString() & " 个")

        Dim hello = all.Find(Function(p) p.Manifest IsNot Nothing AndAlso p.Manifest.Id = "com.example.hello")
        Check("找到 com.example.hello", hello IsNot Nothing, "")
        If hello IsNot Nothing Then
            Check("清单解析正确", hello.Manifest.Name = "示例插件", hello.Manifest.Name)
            Check("入口存在", File.Exists(hello.EntryPath), Path.GetFileName(hello.EntryPath))
            Check("无敏感权限", hello.Manifest.DeclaredPermissions.Count = 0, "")
            ' 读取入口内容
            Dim content = ModPlugin.ReadEntry(hello)
            Check("能读取入口 XAML", content IsNot Nothing AndAlso content.Contains("插件系统工作正常"), "")
        End If

        Console.WriteLine()
        Console.WriteLine("通过 " & Passed.ToString() & "，失败 " & Failed.ToString())
        If Failed > 0 Then Environment.ExitCode = 1
    End Sub
End Module