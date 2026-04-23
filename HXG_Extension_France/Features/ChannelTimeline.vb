Imports ESPRIT.NetApi.Ribbon

''' <summary>
''' Feature: Channel Timeline
''' Hosts the Gantt-style multi-turret timeline inside a dockable ESPRIT pane.
''' Auto-refreshes when the active document changes.
''' </summary>
Public Class ChannelTimelineFeature
    Implements IFeature

    Private Const RIBBON_GROUP_KEY As String = "ChannelTimeline_Group"
    Private Const RIBBON_BTN_KEY As String = "ChannelTimeline_Btn"
    Private Const PANE_KEY As String = "HXG_ChannelTimeline"

    Private ReadOnly _app As ESPRIT.Application
    Private _control As ChannelTimelineControl
    Private _pane As Object   ' Esprit.Pane — late-bound to avoid extra type coupling

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
        TogglePane()
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
            If _pane IsNot Nothing Then
                _app.Panes.RemoveByKey(PANE_KEY)
            End If
        Catch
        End Try
        Try
            If _control IsNot Nothing AndAlso Not _control.IsDisposed Then
                _control.Dispose()
            End If
        Catch
        End Try
        _pane = Nothing
        _control = Nothing
    End Sub

    ' ── Pane lifecycle ────────────────────────────────────────────────────────

    ''' <summary>
    ''' First click: creates the pane and hosts the timeline control.
    ''' Subsequent clicks: toggles pane visibility (and refreshes on show).
    ''' </summary>
    Private Sub TogglePane()
        If _pane Is Nothing Then
            CreatePane()
            Return
        End If

        Try
            If _pane.Visible Then
                _pane.Visible = False
            Else
                _pane.Visible = True
                _pane.Activate()
                If _control IsNot Nothing Then _control.RefreshData()
            End If
        Catch
            ' Pane was likely disposed externally — recreate.
            _pane = Nothing
            _control = Nothing
            CreatePane()
        End Try
    End Sub

    Private Sub CreatePane()
        Try
            If _app.Panes.Contains(PANE_KEY) Then
                _app.Panes.RemoveByKey(PANE_KEY)
            End If
        Catch
        End Try

        _control = New ChannelTimelineControl(_app) With {
            .Dock = System.Windows.Forms.DockStyle.Fill
        }

        _pane = _app.Panes.Add(PANE_KEY)
        _pane.Caption = Strings.Timeline_FormTitle

        Try
            Dim icon As System.Drawing.Icon = ExtensionUtilities.LoadIcon("Timeline.ico")
            If icon IsNot Nothing Then
                _pane.SetIcon(icon.Handle.ToInt64())
            End If
        Catch
        End Try

        _pane.SetControl(_control)
        _pane.Visible = True
        _pane.Activate()
    End Sub

    ' ── Document events ───────────────────────────────────────────────────────

    Private Sub OnDocumentOpened(filePath As String)
        RefreshIfVisible()
    End Sub

    Private Sub OnNewDocumentOpened()
        RefreshIfVisible()
    End Sub

    Private Sub OnDocumentClosed()
        RefreshIfVisible()
    End Sub

    Private Sub RefreshIfVisible()
        If _control Is Nothing OrElse _control.IsDisposed Then Return
        Try
            _control.RefreshData()
        Catch
        End Try
    End Sub

End Class
