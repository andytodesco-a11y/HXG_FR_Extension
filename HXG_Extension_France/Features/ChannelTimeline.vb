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
    Private _linksReadyHooked As Boolean

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

        ' Create the pane upfront so it appears in the pane show/hide menu
        ' immediately and persists across document changes. The control itself
        ' renders a "No program available" message when the document is missing
        ' or has no program.
        EnsurePane()
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        If e.Key <> RIBBON_BTN_KEY Then Return False
        e.Handled = True
        ShowPane()
        Return True
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        Try
            RemoveHandler _app.AfterDocumentOpen, AddressOf OnDocumentOpened
            RemoveHandler _app.AfterNewDocumentOpen, AddressOf OnNewDocumentOpened
            RemoveHandler _app.AfterDocumentClose, AddressOf OnDocumentClosed
        Catch
        End Try
        DetachLinksReady()
        ' Do NOT call Panes.RemoveByKey: leaving the pane registered with a
        ' stable key lets ESPRIT's layout manager remember its docking
        ' position and visibility for the next session.
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
    ''' Ribbon-button fallback: reveals and activates the pane. Visibility is
    ''' primarily managed by ESPRIT's built-in pane show/hide menu.
    ''' </summary>
    Private Sub ShowPane()
        EnsurePane()
        If _pane Is Nothing Then Return
        Try
            _pane.Visible = True
            _pane.Activate()
            If _control IsNot Nothing Then _control.RefreshData()
        Catch
            ' Pane was likely disposed externally — recreate from scratch.
            _pane = Nothing
            _control = Nothing
            EnsurePane()
        End Try
    End Sub

    ''' <summary>
    ''' Creates the pane on first call; a no-op afterward.
    ''' </summary>
    Private Sub EnsurePane()
        If _pane IsNot Nothing Then Return
        CreatePane()
    End Sub

    ''' <summary>
    ''' Reuses the previously-registered pane if ESPRIT still knows about it
    ''' (preserves the user's last docking position and visibility). Falls
    ''' back to creating a fresh pane on first-ever launch.
    ''' </summary>
    Private Sub CreatePane()
        Dim firstLaunch As Boolean
        Try
            If _app.Panes.Contains(PANE_KEY) Then
                _pane = _app.Panes.Item(PANE_KEY)
                firstLaunch = False
            Else
                _pane = _app.Panes.Add(PANE_KEY)
                firstLaunch = True
            End If
        Catch
            _pane = _app.Panes.Add(PANE_KEY)
            firstLaunch = True
        End Try

        _control = New ChannelTimelineControl(_app) With {
            .Dock = System.Windows.Forms.DockStyle.Fill
        }

        _pane.Caption = Strings.Timeline_FormTitle
        _pane.VisibleInShowHideMenu = True

        Try
            Dim icon As System.Drawing.Icon = ExtensionUtilities.LoadIcon("Timeline.ico")
            If icon IsNot Nothing Then
                _pane.SetIcon(icon.Handle.ToInt64())
            End If
        Catch
        End Try

        _pane.SetControl(_control)

        ' Only force visibility on first-ever creation so returning users keep
        ' whatever hidden/shown state they left the pane in.
        If firstLaunch Then
            _pane.Visible = True
            _pane.Activate()
        End If
    End Sub

    ' ── Document events ───────────────────────────────────────────────────────

    Private Sub OnDocumentOpened(filePath As String)
        AttachLinksReady()
        RefreshControl()
    End Sub

    Private Sub OnNewDocumentOpened()
        AttachLinksReady()
        RefreshControl()
    End Sub

    Private Sub OnDocumentClosed()
        DetachLinksReady()
        RefreshControl()
    End Sub

    Private Sub OnLinksReady()
        RefreshControl()
    End Sub

    ''' <summary>
    ''' Subscribes to Document.OnLinksReady so the timeline auto-refreshes
    ''' when ESPRIT finishes recomputing operation links. Called from document
    ''' open events because _app.Document is Nothing until a document exists.
    ''' Idempotent — safe to call repeatedly.
    ''' </summary>
    Private Sub AttachLinksReady()
        If _linksReadyHooked Then Return
        Try
            If _app.Document Is Nothing Then Return
            AddHandler _app.Document.OnLinksReady, AddressOf OnLinksReady
            _linksReadyHooked = True
        Catch
        End Try
    End Sub

    Private Sub DetachLinksReady()
        If Not _linksReadyHooked Then Return
        Try
            RemoveHandler _app.Document.OnLinksReady, AddressOf OnLinksReady
        Catch
        End Try
        _linksReadyHooked = False
    End Sub

    Private Sub RefreshControl()
        EnsurePane()
        If _control Is Nothing OrElse _control.IsDisposed Then Return
        Try
            _control.RefreshData()
        Catch
        End Try
    End Sub

End Class
