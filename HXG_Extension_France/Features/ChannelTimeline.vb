Imports ESPRIT.NetApi.Ribbon

''' <summary>
''' Feature: Channel Timeline
''' Opens a modeless Gantt-style panel for multi-turret channel analysis.
''' Auto-refreshes when the active document changes.
''' </summary>
Public Class ChannelTimelineFeature
    Implements IFeature

    Private Const RIBBON_GROUP_KEY As String = "ChannelTimeline_Group"
    Private Const RIBBON_BTN_KEY As String = "ChannelTimeline_Btn"

    Private ReadOnly _app As ESPRIT.Application
    Private _form As ChannelTimelineForm

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    ' ── IFeature ──────────────────────────────────────────────────────────────

    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Dim group As IRibbonGroup = tab.Groups.Add(RIBBON_GROUP_KEY, Strings.Timeline_GroupLabel)
        group.Items.AddButton(RIBBON_BTN_KEY, Strings.Timeline_ButtonLabel, True, ExtensionUtilities.LoadIcon("Timeline.ico"))

        AddHandler _app.AfterDocumentOpen, AddressOf OnDocumentOpened
        AddHandler _app.AfterNewDocumentOpen, AddressOf OnNewDocumentOpened
        AddHandler _app.AfterDocumentClose, AddressOf OnDocumentClosed
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        If e.Key <> RIBBON_BTN_KEY Then Return False
        e.Handled = True
        ShowOrFocusForm()
        Return True
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        Try
            RemoveHandler _app.AfterDocumentOpen, AddressOf OnDocumentOpened
            RemoveHandler _app.AfterNewDocumentOpen, AddressOf OnNewDocumentOpened
            RemoveHandler _app.AfterDocumentClose, AddressOf OnDocumentClosed
        Catch
        End Try
        Try
            If _form IsNot Nothing AndAlso Not _form.IsDisposed Then
                _form.Close()
            End If
        Catch
        End Try
        _form = Nothing
    End Sub

    ' ── Form lifecycle ────────────────────────────────────────────────────────

    Private Sub ShowOrFocusForm()
        If _form IsNot Nothing AndAlso Not _form.IsDisposed Then
            _form.RefreshData()
            If _form.WindowState = System.Windows.Forms.FormWindowState.Minimized Then
                _form.WindowState = System.Windows.Forms.FormWindowState.Normal
            End If
            _form.Activate()
        Else
            _form = New ChannelTimelineForm(_app)
            AddHandler _form.FormClosed, AddressOf OnFormClosed
            _form.Show()
        End If
    End Sub

    Private Sub OnFormClosed(sender As Object, e As System.Windows.Forms.FormClosedEventArgs)
        _form = Nothing
    End Sub

    ' ── Document events ───────────────────────────────────────────────────────

    Private Sub OnDocumentOpened(filePath As String)
        If _form IsNot Nothing AndAlso Not _form.IsDisposed Then
            _form.RefreshData()
        End If
    End Sub

    Private Sub OnNewDocumentOpened()
        If _form IsNot Nothing AndAlso Not _form.IsDisposed Then
            _form.RefreshData()
        End If
    End Sub

    Private Sub OnDocumentClosed()
        If _form IsNot Nothing AndAlso Not _form.IsDisposed Then
            _form.RefreshData()
        End If
    End Sub

End Class
