Imports System
Imports System.IO
Imports System.Collections.Generic
Imports System.Linq

Module ServerTest
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
        Environment.SetEnvironmentVariable("PCL_TEST_BASE", args(0))

        Console.WriteLine("=== 1. 拉取可下载服务端版本 ===")
        Dim Ok As Boolean = ModServer.FetchRemote()
        Check("拉取成功", Ok, ModServer.RemoteVersions.Count.ToString() & " 个版本")
        If Not Ok Then
            Console.WriteLine()
            Console.WriteLine("通过 " & Passed & "，失败 " & Failed)
            Environment.ExitCode = 1
            Return
        End If

        Dim latest = ModServer.RemoteVersions(0)
        Console.WriteLine("  最新: " & latest.Id & "  " & StringUtils.FormatByteSize(latest.Size) & "  " & latest.ReleaseTime)
        Check("最新版本有下载地址", Not String.IsNullOrEmpty(latest.Url), latest.Url.Substring(0, Math.Min(50, latest.Url.Length)))
        Check("大小合理(>10MB)", latest.Size > 10 * 1024 * 1024, (latest.Size / 1048576).ToString() & " MB")
        Check("有 SHA1 校验值", Not String.IsNullOrEmpty(latest.Sha1), latest.Sha1.Substring(0, 12))

        ' 关键：确认是服务端而不是客户端
        Check("URL 是 server 不是 client", Not latest.Url.ToLower().Contains("client"), "服务端专用")

        Console.WriteLine()
        Console.WriteLine("=== 2. 本地扫描（空目录）===")
        ModServer.ScanLocal()
        Check("空目录扫描不崩溃", True, ModServer.LocalVersions.Count.ToString() & " 个")

        Console.WriteLine()
        Console.WriteLine("=== 3. 创建版本目录 ===")
        Dim v As New ModServer.ServerVersion With {.Name = "test-" & latest.Id, .MemoryMB = 4096, .OnlineMode = False}
        ModServer.SaveConfig(v)
        Check("配置已写入", File.Exists(v.ConfigPath), "pcl-server.json")

        ModServer.ScanLocal()
        Dim found = ModServer.LocalVersions.Find(Function(x) x.Name = v.Name)
        Check("扫描能发现新目录", found IsNot Nothing, "")
        If found IsNot Nothing Then
            Check("内存配置正确", found.MemoryMB = 4096, found.MemoryMB.ToString() & " MB")
            Check("离线模式正确", found.OnlineMode = False, "")
            Check("未安装状态", Not found.Installed, found.ReadyText)
        End If

        Console.WriteLine()
        Console.WriteLine("=== 4. EULA 流程 ===")
        Check("未同意前 EulaAccepted=False", Not found.EulaAccepted, "")
        ModServer.AcceptEula(found)
        Check("同意后 EulaAccepted=True", found.EulaAccepted, "")
        Check("eula.txt 已写入", File.Exists(found.EulaPath), "")
        If File.Exists(found.EulaPath) Then
            Dim content = File.ReadAllText(found.EulaPath)
            Check("eula.txt 内容正确", content.Contains("eula=true"), "")
            Check("含 EULA 链接", content.Contains("aka.ms/MinecraftEULA"), "")
        End If

        Console.WriteLine()
        Console.WriteLine("=== 5. server.properties 生成 ===")
        ModServer.WriteProperties(found)
        Dim props = Path.Combine(found.Dir, "server.properties")
        Check("文件已生成", File.Exists(props), "")
        If File.Exists(props) Then
            Dim pc = File.ReadAllText(props)
            Check("端口正确", pc.Contains("server-port=25565"), "")
            Check("离线模式正确", pc.Contains("online-mode=false"), "")
        End If

        Console.WriteLine()
        Console.WriteLine("=== 6. 启动参数 ===")
        Dim a = ModServer.BuildArguments(found)
        Console.WriteLine("  " & a)
        Check("含内存参数", a.Contains("-Xmx4096M"), "")
        Check("含 jar 参数", a.Contains("-jar"), "")
        Check("含 nogui", a.Contains("nogui"), "")

        Console.WriteLine()
        Console.WriteLine("=== 7. 内存下限保护 ===")
        found.MemoryMB = 0
        Dim a2 = ModServer.BuildArguments(found)
        Check("内存填0时用下限", a2.Contains("-Xmx1024M"), a2)

        Console.WriteLine()
        Console.WriteLine("=== 清理 ===")
        ModServer.DeleteVersion(found)
        Check("目录已删除", Not Directory.Exists(found.Dir), "")

        Console.WriteLine()
        Console.WriteLine("通过 " & Passed & "，失败 " & Failed)
        If Failed > 0 Then Environment.ExitCode = 1
    End Sub
End Module