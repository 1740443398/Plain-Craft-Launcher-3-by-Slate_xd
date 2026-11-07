Imports System
Imports System.IO

Module JavaHostTest
    Dim P As Integer = 0
    Dim F As Integer = 0

    Sub Check(n As String, c As Boolean, d As String)
        If c Then
            P += 1
            Console.WriteLine("  OK    " & n & "  " & d)
        Else
            F += 1
            Console.WriteLine("  FAIL  " & n & "  " & d)
        End If
    End Sub

    Sub Main(args As String())
        Environment.SetEnvironmentVariable("PCL_TEST_BASE", args(0))
        Dim jarSrc = args(1)

        Console.WriteLine("=== 1. 准备插件目录 ===")
        Dim plugDir = Path.Combine(Environment.GetEnvironmentVariable("PCL_TEST_BASE"), "PCL\Plugins\javademo")
        Directory.CreateDirectory(plugDir)
        File.Copy(jarSrc, Path.Combine(plugDir, "plugin.jar"), True)
        File.WriteAllText(Path.Combine(plugDir, "plugin.json"),
            "{""id"":""com.example.javademo"",""name"":""Java 示例"",""version"":""1.0.0"",""runtime"":""java""}")
        Check("目录已建", Directory.Exists(plugDir), plugDir)

        Console.WriteLine()
        Console.WriteLine("=== 2. 扫描识别为 Java 插件 ===")
        ModPlugin.Scan()
        Dim e = ModPlugin.Plugins.FirstOrDefault(Function(x) x.Manifest IsNot Nothing AndAlso x.Manifest.Id = "com.example.javademo")
        Check("扫描到插件", e IsNot Nothing, If(e IsNot Nothing, e.DisplayName, ""))
        If e Is Nothing Then Environment.Exit(1)
        Check("识别为 Java", e.IsJava, "")
        Check("jar 就绪", e.JavaReady, e.JarPath)

        Console.WriteLine()
        Console.WriteLine("=== 3. 启动 Java 进程 ===")
        Dim inst = ModPluginJava.Start(e)
        Check("进程已启动", inst IsNot Nothing AndAlso inst.IsAlive, If(inst IsNot Nothing, "pid " & inst.Process.Id, ""))
        If inst Is Nothing Then Environment.Exit(1)

        Console.WriteLine()
        Console.WriteLine("=== 4. 等待插件响应握手 ===")
        Dim waited = 0
        While waited < 6000 AndAlso String.IsNullOrEmpty(inst.LastRequest)
            System.Threading.Thread.Sleep(200)
            waited += 200
        End While
        Check("收到插件请求", Not String.IsNullOrEmpty(inst.LastRequest), "action=" & inst.LastRequest)
        Check("IsRunning 正确", ModPluginJava.IsRunning("com.example.javademo"), "")

        Console.WriteLine()
        Console.WriteLine("=== 5. 停止插件 ===")
        ModPluginJava.Stop("com.example.javademo")
        System.Threading.Thread.Sleep(600)
        Check("已停止", Not ModPluginJava.IsRunning("com.example.javademo"), "")

        Console.WriteLine()
        Console.WriteLine("=== 6. StopAll 兜底 ===")
        ModPluginJava.StopAll()
        Check("StopAll 不报错", True, "剩余 " & ModPluginJava.Running.Count & " 个")

        Console.WriteLine()
        Console.WriteLine("通过 " & P & "，失败 " & F)
        If F > 0 Then Environment.ExitCode = 1
    End Sub
End Module