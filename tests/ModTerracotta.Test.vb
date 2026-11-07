Imports System
Imports System.IO

Module E2ETest
    Sub Main(args As String())
        Dim target = args(0)
        Console.WriteLine("目标路径: " & target)
        Console.WriteLine("")
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Dim ok = ModTerracotta.EnsureInstalled(target,
            Sub(r As Double, text As String)
                Console.Write(vbCr & "  [" & Math.Round(r*100).ToString().PadLeft(3) & "%] " & text & "        ")
            End Sub)
        sw.Stop()
        Console.WriteLine("")
        Console.WriteLine("")
        Console.WriteLine("结果: " & If(ok, "✔ 成功", "✘ 失败"))
        Console.WriteLine("耗时: " & Math.Round(sw.Elapsed.TotalSeconds, 1) & " 秒")
        If File.Exists(target) Then
            Dim fi As New FileInfo(target)
            Console.WriteLine("文件: " & fi.Name & "  " & Math.Round(fi.Length/1048576.0, 2) & " MB")
            Dim fs = File.OpenRead(target)
            Dim buf(1) As Byte
            fs.Read(buf, 0, 2)
            fs.Close()
            Console.WriteLine("文件头: " & System.Text.Encoding.ASCII.GetString(buf))
        End If
        If Not ok Then Environment.ExitCode = 1
    End Sub
End Module