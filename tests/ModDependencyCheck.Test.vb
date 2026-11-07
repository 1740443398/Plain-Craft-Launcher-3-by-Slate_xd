Imports System
Imports System.Collections.Generic
Imports System.IO

Public Class McInstance
    Public Property Name As String = "test"
    Public Property PathIndie As String = ""
End Class

Module DepTest
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
        Dim base = args(0)
        Dim inst As New McInstance With {.PathIndie = base}

        Dim r = ModDependencyCheck.Check(inst)
        Console.WriteLine("=== 检查结果 ===")
        Console.WriteLine("  参与检查: " & r.CheckedCount.ToString())
        Console.WriteLine("  解析失败: " & r.UnparsableCount.ToString())
        Console.WriteLine("  确定问题: " & r.Issues.Count.ToString())
        Console.WriteLine("  无法确认: " & r.Uncertain.Count.ToString())
        Console.WriteLine("")
        For Each i In r.Issues
            Console.WriteLine("  [" & i.Kind.ToString() & "] " & i.DisplayText)
        Next
        Console.WriteLine("")

        Check("参与检查 3 个", r.CheckedCount = 3, r.CheckedCount.ToString())
        Check("有确定问题", r.HasIssues, r.Issues.Count.ToString())
        ' Mod A 的 cloth-config 版本不符
        Dim hasMismatch = False
        For Each i In r.Issues
            If i.Kind = ModDependencyCheck.IssueKind.VersionMismatch AndAlso i.DependencyId = "cloth-config" Then hasMismatch = True
        Next
        Check("检出 cloth-config 版本不符", hasMismatch, "")
        ' Mod C 的 sodium 缺失
        Dim hasMissing = False
        For Each i In r.Issues
            If i.Kind = ModDependencyCheck.IssueKind.Missing AndAlso i.DependencyId = "sodium" Then hasMissing = True
        Next
        Check("检出 sodium 缺失", hasMissing, "")
        ' fabric-api / minecraft / fabricloader 不应报缺失
        Dim badSkip = False
        For Each i In r.Issues
            If i.DependencyId = "fabric-api" OrElse i.DependencyId = "minecraft" OrElse i.DependencyId = "fabricloader" Then badSkip = True
        Next
        Check("正确跳过加载器/MC 自身", Not badSkip, "")

        Console.WriteLine()
        Console.WriteLine("通过 " & Passed.ToString() & "，失败 " & Failed.ToString())
        If Failed > 0 Then Environment.ExitCode = 1
    End Sub
End Module