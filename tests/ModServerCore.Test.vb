Imports System
Imports System.Collections.Generic
Imports System.Linq

Module FmTest
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
        Console.WriteLine("=== 1. 拉取核心列表 ===")
        Dim ok = ModServerCore.FetchCores()
        Check("拉取成功", ok, ModServerCore.Cores.Count.ToString() & " 个核心")
        If Not ok Then Environment.Exit(1)

        Console.WriteLine()
        Console.WriteLine("  按类别:")
        For Each k In {ModServerCore.CoreKind.Vanilla, ModServerCore.CoreKind.Plugin, ModServerCore.CoreKind.Modded, ModServerCore.CoreKind.Bedrock, ModServerCore.CoreKind.Proxy}
            Dim list = ModServerCore.GetByKind(k)
            Console.WriteLine("    " & k.ToString().PadRight(10) & list.Count & " 个: " & String.Join(", ", list.Select(Function(c) c.Name)))
        Next

        Check("有原版", ModServerCore.GetByKind(ModServerCore.CoreKind.Vanilla).Count >= 1, "")
        Check("有插件服", ModServerCore.GetByKind(ModServerCore.CoreKind.Plugin).Count >= 3, "")
        Check("有模组服", ModServerCore.GetByKind(ModServerCore.CoreKind.Modded).Count >= 4, "")
        Check("有基岩版", ModServerCore.GetByKind(ModServerCore.CoreKind.Bedrock).Count >= 1, "")
        Check("有代理端", ModServerCore.GetByKind(ModServerCore.CoreKind.Proxy).Count >= 1, "")

        Console.WriteLine()
        Console.WriteLine("=== 2. 每个核心都有版本列表 ===")
        Dim noVer = 0
        For Each c In ModServerCore.Cores
            If c.Versions.Count = 0 Then noVer += 1
        Next
        Check("所有核心都有版本", noVer = 0, "无版本的: " & noVer)

        Console.WriteLine()
        Console.WriteLine("=== 3. 取构建（Paper 1.21.4）===")
        Dim builds = ModServerCore.FetchBuilds("Paper", "1.21.4")
        Check("有构建", builds.Count > 0, builds.Count.ToString() & " 个")
        If builds.Count > 0 Then
            Dim b = builds(0)
            Console.WriteLine("    build=" & b.Build & "  sha1=" & b.Sha1.Substring(0, Math.Min(12, b.Sha1.Length)))
            Console.WriteLine("    url=" & b.Url)
            Check("有下载地址", Not String.IsNullOrEmpty(b.Url), "")
            Check("有 SHA1 校验", Not String.IsNullOrEmpty(b.Sha1), "")
            Check("URL 指向 fastmirror", b.Url.Contains("fastmirror.net"), "")
        End If

        Console.WriteLine()
        Console.WriteLine("=== 4. 多核心构建测试 ===")
        For Each cn In {"Vanilla", "Fabric", "Purpur", "Forge", "Arclight"}
            Dim bs = ModServerCore.FetchBuilds(cn, "1.20.1")
            If bs.Count > 0 Then
                Console.WriteLine("    OK   " & cn.PadRight(12) & bs(0).Build & "  " & bs(0).Url.Substring(0, Math.Min(60, bs(0).Url.Length)))
            Else
                Console.WriteLine("    --   " & cn.PadRight(12) & "(无构建)")
            End If
        Next

        Console.WriteLine()
        Console.WriteLine("=== 5. Mojang 官方直连 ===")
        Dim v = ModServerCore.FetchVanillaOfficial("1.21.4")
        Check("拿到官方地址", v IsNot Nothing AndAlso Not String.IsNullOrEmpty(v.Url), If(v IsNot Nothing, v.Url.Substring(0, Math.Min(60, v.Url.Length)), ""))
        If v IsNot Nothing Then
            Check("官方 URL 指向 mojang", v.Url.Contains("mojang") OrElse v.Url.Contains("piston"), "")
            Check("有大小信息", v.Size > 0, (v.Size / 1048576).ToString() & " MB")
        End If

        Console.WriteLine()
        Console.WriteLine("通过 " & Passed & "，失败 " & Failed)
        If Failed > 0 Then Environment.ExitCode = 1
    End Sub
End Module