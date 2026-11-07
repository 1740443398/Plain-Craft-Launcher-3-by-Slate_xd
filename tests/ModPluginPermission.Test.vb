Imports System
Imports System.IO
Imports System.Linq

Module PermTest
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

    Function MakeEntry(id As String, perms As String()) As ModPlugin.PluginEntry
        Return New ModPlugin.PluginEntry With {
            .Directory = Path.Combine(Environment.GetEnvironmentVariable("PCL_TEST_BASE"), "PCL\Plugins", id),
            .Manifest = New ModPlugin.PluginManifest With {
                .Id = id, .Name = id, .Version = "1.0.0", .Permissions = perms
            }
        }
    End Function

    Sub Main(args As String())
        Environment.SetEnvironmentVariable("PCL_TEST_BASE", args(0))

        Console.WriteLine("=== 1. 权限清单 ===")
        Check("定义了 9 项权限", ModPluginPermission.AllPerms.Count = 9, ModPluginPermission.AllPerms.Count.ToString())
        Check("能找到 network", ModPluginPermission.FindByKey("network") IsNot Nothing, "")
        Check("未知 key 返回空", ModPluginPermission.FindByKey("nonexistent") Is Nothing, "")

        Console.WriteLine()
        Console.WriteLine("=== 2. 默认全部未授权 ===")
        Dim e1 = MakeEntry("test1", {"network", "fileRead"})
        Check("未授权 network", Not ModPluginPermission.IsGranted("test1", ModPluginPermission.Perm.Network), "")
        Check("未授权 fileRead", Not ModPluginPermission.IsGranted("test1", ModPluginPermission.Perm.FileRead), "")
        Check("IsFullyGranted=False", Not ModPluginPermission.IsFullyGranted(e1), "")

        Console.WriteLine()
        Console.WriteLine("=== 3. 授权后生效 ===")
        ModPluginPermission.GrantAll(e1)
        Check("已授权 network", ModPluginPermission.IsGranted("test1", ModPluginPermission.Perm.Network), "")
        Check("已授权 fileRead", ModPluginPermission.IsGranted("test1", ModPluginPermission.Perm.FileRead), "")
        Check("IsFullyGranted=True", ModPluginPermission.IsFullyGranted(e1), "")
        Check("未申请的仍拒绝", Not ModPluginPermission.IsGranted("test1", ModPluginPermission.Perm.FileWrite), "")

        Console.WriteLine()
        Console.WriteLine("=== 4. 撤销 ===")
        ModPluginPermission.RevokeAll("test1")
        Check("撤销后拒绝", Not ModPluginPermission.IsGranted("test1", ModPluginPermission.Perm.Network), "")

        Console.WriteLine()
        Console.WriteLine("=== 5. 敏感路径判定 ===")
        Dim baseDir = Path.Combine(Environment.GetEnvironmentVariable("PCL_TEST_BASE"), "PCL")
        Directory.CreateDirectory(baseDir)
        ' 造一个含 token 的配置文件
        File.WriteAllText(Path.Combine(baseDir, "accounts.json"), "{""accessToken"":""secret123""}")
        File.WriteAllText(Path.Combine(baseDir, "harmless.json"), "{""name"":""hello""}")

        Dim e2 = MakeEntry("test2", {"fileRead"})
        ModPluginPermission.GrantAll(e2)
        Check("有 fileRead 权限", ModPluginPermission.IsGranted("test2", ModPluginPermission.Perm.FileRead), "")

        Console.WriteLine()
        Console.WriteLine("  通过反射调用私有的敏感判定")
        Dim mi = GetType(ModPluginJava).GetMethod("IsSensitivePath", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Static)
        Check("找到 IsSensitivePath", mi IsNot Nothing, "")
        If mi IsNot Nothing Then
            Check("accounts.json 被判敏感", CBool(mi.Invoke(Nothing, New Object() {Path.Combine(baseDir, "accounts.json")})), "含 accessToken")
            Check("harmless.json 不敏感", Not CBool(mi.Invoke(Nothing, New Object() {Path.Combine(baseDir, "harmless.json")})), "无敏感特征")
            Check("api.json 被判敏感", CBool(mi.Invoke(Nothing, New Object() {Path.Combine(baseDir, "api.json")})), "文件名黑名单")
            Check("log1.txt 被判敏感", CBool(mi.Invoke(Nothing, New Object() {Path.Combine(baseDir, "Log1.txt")})), "文件名黑名单")
            Check(".pfx 被判敏感", CBool(mi.Invoke(Nothing, New Object() {Path.Combine(baseDir, "cert.pfx")})), "扩展名黑名单")
        End If

        Console.WriteLine()
        Console.WriteLine("=== 6. 路径穿越拦截 ===")
        Dim mr = GetType(ModPluginJava).GetMethod("ResolvePluginFile", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Static)
        Check("找到 ResolvePluginFile", mr IsNot Nothing, "")
        If mr IsNot Nothing Then
            Dim inst = New ModPluginJava.JavaInstance With {.Entry = e2}
            Check("拒绝 ..", mr.Invoke(Nothing, New Object() {inst, "../../secret.txt", False}) Is Nothing, "../../secret.txt")
            Check("拒绝绝对路径", mr.Invoke(Nothing, New Object() {inst, "C:\Windows\win.ini", False}) Is Nothing, "C:\...")
            Check("拒绝 UNC", mr.Invoke(Nothing, New Object() {inst, "\\server\share\a.txt", False}) Is Nothing, "\\server")
        End If

        Console.WriteLine()
        Console.WriteLine("通过 " & P & "，失败 " & F)
        If F > 0 Then Environment.ExitCode = 1
    End Sub
End Module