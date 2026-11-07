Imports System
Imports System.IO
Imports System.Collections.Generic

Module LocalE2E
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
        Dim localUrl = args(0)
        Dim target = args(1)
        Dim badUrl = args(2)

        Console.WriteLine("=== 场景 1：正常下载安装 ===")
        Remove(target)
        Dim sw = Diagnostics.Stopwatch.StartNew()
        Dim ok = ModTerracotta.EnsureInstalled(target, Nothing, New String() {localUrl})
        sw.Stop()
        Check("安装成功", ok, "")
        Check("文件存在", File.Exists(target), "")
        If File.Exists(target) Then
            Dim fi As New FileInfo(target)
            Check("大小合理", fi.Length > 1000000, Math.Round(fi.Length/1048576.0,2).ToString() & " MB")
            Dim fs = File.OpenRead(target)
            Dim buf(1) As Byte
            fs.Read(buf, 0, 2)
            fs.Close()
            Check("是 PE 文件", System.Text.Encoding.ASCII.GetString(buf) = "MZ", "")
        End If
        Console.WriteLine("  耗时 " & Math.Round(sw.Elapsed.TotalSeconds,1) & " 秒")

        Console.WriteLine("")
        Console.WriteLine("=== 场景 2：哈希不匹配应拒绝 ===")
        Dim target2 = target & ".bad"
        Remove(target2)
        ' 用一个内容不同但大小相近的假包
        Dim ok2 = ModTerracotta.EnsureInstalled(target2, Nothing, New String() {badUrl})
        Check("应拒绝并返回 False", Not ok2, "")
        Check("不应留下文件", Not File.Exists(target2), "")

        Console.WriteLine("")
        Console.WriteLine("=== 场景 3：已存在应直接跳过 ===")
        Dim sw3 = Diagnostics.Stopwatch.StartNew()
        Dim ok3 = ModTerracotta.EnsureInstalled(target, Nothing, New String() {"http://127.0.0.1:1/never"})
        sw3.Stop()
        Check("返回 True", ok3, "")
        Check("未重新下载（耗时 < 1 秒）", sw3.Elapsed.TotalSeconds < 1.0, Math.Round(sw3.Elapsed.TotalSeconds,2).ToString() & " 秒")

        Console.WriteLine("")
        Console.WriteLine("通过 " & Passed.ToString() & "，失败 " & Failed.ToString())
        If Failed > 0 Then Environment.ExitCode = 1
    End Sub

    Sub Remove(p As String)
        Try
            If File.Exists(p) Then File.Delete(p)
        Catch
        End Try
    End Sub
End Module