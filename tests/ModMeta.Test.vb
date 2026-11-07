Imports System
Imports System.Collections.Generic

Module MetaTest
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

    Sub Main()
        Dim base = If(Environment.GetCommandLineArgs().Length > 1, Environment.GetCommandLineArgs()(1), System.IO.Directory.GetCurrentDirectory())
        If Not base.EndsWith("\") Then base &= "\"

        Console.WriteLine("=== Fabric ===")
        Dim f = ModMeta.ReadMeta(base & "fabric.jar")
        Check("解析成功", f.Parsed, f.FailureReason)
        Check("ID", f.PrimaryId = "examplemod", f.PrimaryId)
        Check("名称", f.Name = "Example Mod", f.Name)
        Check("版本", f.Version = "1.2.3", f.Version)
        Check("加载器", f.Loader = ModMeta.ModLoaderKind.Fabric, f.Loader.ToString())
        Check("依赖数(应只留 cloth-config)", f.Dependencies.Count = 1, "实际 " & f.Dependencies.Count.ToString())
        If f.Dependencies.Count > 0 Then
            Check("依赖 ID", f.Dependencies(0).ModId = "cloth-config", f.Dependencies(0).ModId)
            Check("依赖版本", f.Dependencies(0).Requirement = ">=11.0.0", f.Dependencies(0).Requirement)
        End If

        Console.WriteLine("=== Forge TOML ===")
        Dim g = ModMeta.ReadMeta(base & "forge.jar")
        Check("解析成功", g.Parsed, g.FailureReason)
        Check("ID", g.PrimaryId = "forgemod", g.PrimaryId)
        Check("名称", g.Name = "Forge Mod", g.Name)
        Check("版本", g.Version = "2.0.0", g.Version)
        Check("加载器", g.Loader = ModMeta.ModLoaderKind.Forge, g.Loader.ToString())
        Check("依赖数(应只留 jei)", g.Dependencies.Count = 1, "实际 " & g.Dependencies.Count.ToString())
        If g.Dependencies.Count > 0 Then Check("依赖 ID", g.Dependencies(0).ModId = "jei", g.Dependencies(0).ModId)

        Console.WriteLine("=== 旧版 Forge ===")
        Dim h = ModMeta.ReadMeta(base & "legacy.jar")
        Check("解析成功", h.Parsed, h.FailureReason)
        Check("ID", h.PrimaryId = "oldmod", h.PrimaryId)
        Check("名称", h.Name = "Old Mod", h.Name)

        Console.WriteLine("=== 无声明 JAR ===")
        Dim p = ModMeta.ReadMeta(base & "plain.jar")
        Check("解析失败(预期)", Not p.Parsed, "reason=" & p.FailureReason)

        Console.WriteLine("=== 不存在的文件 ===")
        Dim n = ModMeta.ReadMeta(base & "nope.jar")
        Check("解析失败(预期)", Not n.Parsed, "reason=" & n.FailureReason)

        Console.WriteLine()
        Console.WriteLine("通过 " & Passed.ToString() & "，失败 " & Failed.ToString())
        If Failed > 0 Then Environment.ExitCode = 1
    End Sub
End Module