Imports System
Module NatTest
    Sub Main()
        Console.WriteLine("开始检测 NAT 类型（最长约 10 秒）...")
        Console.WriteLine("")
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Dim r = ModNatCheck.Detect()
        sw.Stop()
        Console.WriteLine("耗时: " & sw.ElapsedMilliseconds.ToString() & " ms")
        Console.WriteLine("类型: " & r.Type.ToString())
        Console.WriteLine("公网地址: " & If(r.PublicEndpoint = "", "(无)", r.PublicEndpoint))
        Console.WriteLine("说明: " & r.Detail)
        Console.WriteLine("")
        If r.Type = ModNatCheck.NatType.Unknown OrElse r.Type = ModNatCheck.NatType.UnsupportedServer Then
            Console.WriteLine("[!] 检测未成功，可能当前网络屏蔽了 STUN")
            Environment.ExitCode = 2
        Else
            Console.WriteLine("[OK] 检测成功")
        End If
    End Sub
End Module