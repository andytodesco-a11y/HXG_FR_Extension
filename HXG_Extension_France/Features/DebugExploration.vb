Imports ESPRIT.NetApi.Ribbon
Imports EspritSolids


Public Class DebugExploration
    Implements IFeature

    Private Const RIBBON_GROUP_KEY As String = "DebugExploration_Group"
    Private Const RIBBON_BUTTON_KEY As String = "DebugExploration_Btn"
    Private Const LOG_SOURCE As String = "DebugExploration"

    Private ReadOnly _app As ESPRIT.Application

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    ' ── IFeature ─────────────────────────────────────────────────────────────
    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Dim group As IRibbonGroup = tab.Groups.Add(RIBBON_GROUP_KEY, "DebugGroup")
        Dim icon As System.Drawing.Icon = Nothing
        Try
            Dim assemblyDir As String = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location)
            Dim iconPath As String = System.IO.Path.Combine(assemblyDir, "HXG_Extension_France_Large.ico")
            If System.IO.File.Exists(iconPath) Then
                icon = New System.Drawing.Icon(iconPath)
            End If
        Catch
            ' Continue without icon if loading fails
        End Try
        group.Items.AddButton(RIBBON_BUTTON_KEY, "DebugExploration", True, icon)
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        If e.Key = RIBBON_BUTTON_KEY Then
            e.Handled = True

            Sandbox()

            Return True
        End If
        Return False
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        ' No per-feature cleanup needed; the tab is removed by Main.
    End Sub

    Private Sub Sandbox()

        AnalyzeSelectedFace()

    End Sub

    Private Sub AnalyzeSelectedFace()
        Dim doc As ESPRIT.Document = _app.Document
        Dim selection As ESPRIT.Group = doc.Group

        If selection.Count = 0 Then
            LogWarning("No selection to analyze.")
            Return
        End If
        If doc.Group.Count > 1 Then
            LogWarning("Multiple elements selected. Please select a single solid only.")
            Return
        End If

        Dim face As ISolidFace = TryCast(doc.Group.Item(1), EspritSolids.ISolidFace)
        If face Is Nothing Then
            LogWarning("The selected element is not a solid.")
            Return
        End If

        ExtensionUtilities.AnalyzeFace(face, _app)
    End Sub




    Private Sub LogWarning(msg As String)
        _app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeWarning, LOG_SOURCE, msg)
    End Sub

End Class
