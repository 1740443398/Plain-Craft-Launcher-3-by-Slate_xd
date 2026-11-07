Imports System
Imports System.IO

Module SkinTest
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

        Console.WriteLine("=== 1. 查询 authlib-injector 版本 ===")
        Dim info = ModSkin.FetchInfo()
        Check("拿到版本信息", info IsNot Nothing, If(info IsNot Nothing, "v" & info.Version & " build " & info.BuildNumber, ""))
        If info IsNot Nothing Then
            Check("有下载地址", Not String.IsNullOrEmpty(info.DownloadUrl), info.DownloadUrl.Substring(0, Math.Min(60, info.DownloadUrl.Length)))
            Check("有 SHA256", Not String.IsNullOrEmpty(info.Sha256), info.Sha256.Substring(0, Math.Min(16, info.Sha256.Length)))
        End If

        Console.WriteLine()
        Console.WriteLine("=== 2. 下载并校验 ===")
        Dim ok = ModSkin.EnsureJar(True)
        Check("下载成功", ok, "")
        Check("文件存在", ModSkin.IsReady, ModSkin.JarPath)
        If ModSkin.IsReady Then
            Dim fi = New FileInfo(ModSkin.JarPath)
            Check("文件够大", fi.Length > 100 * 1024, Math.Round(fi.Length / 1024.0, 1) & " KB")
            ' 校验是 jar
            Dim head(1) As Byte
            Using fs = File.OpenRead(ModSkin.JarPath) : fs.Read(head, 0, 2) : End Using
            Check("是有效 jar", head(0) = &H50 AndAlso head(1) = &H4B, "PK 头")
        End If

        Console.WriteLine()
        Console.WriteLine("=== 3. 构造 agent 参数 ===")
        Dim arg = ModSkin.BuildAgentArg("https://skin.example.com/api/yggdrasil")
        Check("参数非空", Not String.IsNullOrEmpty(arg), arg)
        Check("含 javaagent", arg.Contains("-javaagent:"), "")
        Check("含引号（路径有空格）", arg.Contains(ChrW(34)), "")
        Check("含服务地址", arg.Contains("skin.example.com"), "")
        Check("HasAgent 能识别", ModSkin.HasAgent(arg), "")
        Check("空参数不误判", Not ModSkin.HasAgent(""), "")

        Console.WriteLine()
        Console.WriteLine("通过 " & P & "，失败 " & F)
        If F > 0 Then Environment.ExitCode = 1
    End Sub
End Module