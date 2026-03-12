Imports ESPRIT.NetApi.Ribbon

''' <summary>
''' Feature: Contouring Compensation Checker
''' Adds a group to the "ContextTab.CreateTechPage" ribbon tab.
''' Checks that contour compensation settings are consistent on selected operations.
''' </summary>
Public Class ContouringCompCheckerFeature
    Implements IFeature

    Private Const TARGET_TAB_KEY As String = "ContextTab.CreateTechPage"
    Private Const RIBBON_GROUP_KEY As String = "ContouringCompChecker_Group"
    Private Const RIBBON_SPLITBTN_KEY As String = "ContouringCompChecker_SplitBtn"
    Private Const RIBBON_BTN1_KEY As String = "ContouringCompChecker_Btn1"
    Private Const RIBBON_BTN2_KEY As String = "ContouringCompChecker_Btn2"
    Private Const RIBBON_BTN3_KEY As String = "ContouringCompChecker_Btn3"

    Private ReadOnly _app As ESPRIT.Application
    Private _splitButton As IRibbonSplitButton
    Private _techUtil As EspritTechnology.TechnologyUtility
    Private _currentTechnology As EspritTechnology.ITechnology

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    ''' <summary>
    ''' Adds the feature's ribbon group to the "ContextTab.CreateTechPage" tab.
    ''' The <paramref name="tab"/> argument (the HXG extension tab) is intentionally ignored.
    ''' </summary>
    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Try
            Dim ribbon As IRibbon = DirectCast(_app.Ribbon, IRibbon)
            If Not ribbon.Tabs.Contains(TARGET_TAB_KEY) Then Exit Sub

            Dim targetTab As IRibbonTab = ribbon.Tabs.Item(TARGET_TAB_KEY)
            If targetTab.Groups.Contains(RIBBON_GROUP_KEY) Then Exit Sub

            Dim group As IRibbonGroup = targetTab.Groups.Add(RIBBON_GROUP_KEY, "Comp Check")

            Dim buttons As New List(Of IRibbonButtonInfo) From {
                New RibbonButtonInfo() With {.Key = RIBBON_BTN1_KEY, .Caption = "Action 1", .Icon = LoadIcon("PlaceHolder.ico")},
                New RibbonButtonInfo() With {.Key = RIBBON_BTN2_KEY, .Caption = "Action 2", .Icon = LoadIcon("PlaceHolder.ico")},
                New RibbonButtonInfo() With {.Key = RIBBON_BTN3_KEY, .Caption = "Action 3", .Icon = LoadIcon("PlaceHolder.ico")}
            }
            _splitButton = group.Items.AddSplitButton(RIBBON_SPLITBTN_KEY, True, buttons)
            _splitButton.Visible = False
        Catch
            ' Tab may not exist in the current ESPRIT context — silently skip.
        End Try

        AddHandler _app.AfterDocumentOpen, AddressOf OnAfterDocumentOpen
        AddHandler _app.AfterNewDocumentOpen, AddressOf OnAfterNewDocumentOpen

        If _app.Document IsNot Nothing Then
            SubscribeToTechUtil()
        End If
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        Select Case e.Key
            Case RIBBON_BTN1_KEY
                e.Handled = True
                OnAction1()
                Return True
            Case RIBBON_BTN2_KEY
                e.Handled = True
                OnAction2()
                Return True
            Case RIBBON_BTN3_KEY
                e.Handled = True
                OnAction3()
                Return True
        End Select
        Return False
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        Try
            RemoveHandler _app.AfterDocumentOpen, AddressOf OnAfterDocumentOpen
            RemoveHandler _app.AfterNewDocumentOpen, AddressOf OnAfterNewDocumentOpen
        Catch
        End Try
        Try
            If _techUtil IsNot Nothing Then
                RemoveHandler _techUtil.OnTechPageUiLoaded, AddressOf OnTechPageUiLoaded
            End If
        Catch
        End Try
        Try
            Dim ribbon As IRibbon = DirectCast(_app.Ribbon, IRibbon)
            If Not ribbon.Tabs.Contains(TARGET_TAB_KEY) Then Exit Sub

            Dim targetTab As IRibbonTab = ribbon.Tabs.Item(TARGET_TAB_KEY)
            If targetTab.Groups.Contains(RIBBON_GROUP_KEY) Then
                targetTab.Groups.Remove(RIBBON_GROUP_KEY)
            End If
        Catch
            ' Ignore cleanup errors
        End Try
    End Sub

    ' ── Document events ───────────────────────────────────────────────────────

    Private Sub OnAfterDocumentOpen(ByVal FilePath As String)
        SubscribeToTechUtil()
    End Sub

    Private Sub OnAfterNewDocumentOpen()
        SubscribeToTechUtil()
    End Sub

    Private Sub SubscribeToTechUtil()
        If _techUtil IsNot Nothing Then Exit Sub
        Try
            _techUtil = CType(_app.Document.TechnologyUtility, EspritTechnology.TechnologyUtility)
            AddHandler _techUtil.OnTechPageUiLoaded, AddressOf OnTechPageUiLoaded
        Catch
        End Try
    End Sub

    ' ── Event handler ─────────────────────────────────────────────────────────

    Private Sub OnTechPageUiLoaded(ByVal Technology As Object)
        If _splitButton Is Nothing Then Exit Sub
        Try
            Dim tech As EspritTechnology.ITechnology = CType(Technology, EspritTechnology.ITechnology)
            _currentTechnology = tech
            _splitButton.Visible = tech.HasClCode(245) AndAlso tech.HasClCode(780)
        Catch ex As Exception
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                "ContouringCompChecker",
                $"OnTechPageUiLoaded error: {ex.Message}")
        End Try
    End Sub

    ' ── Core logic ────────────────────────────────────────────────────────────

    Private Sub OnAction1()
        AppendToOperationName("Btn1")
    End Sub

    Private Sub OnAction2()
        AppendToOperationName("Btn2")
    End Sub

    Private Sub OnAction3()
        AppendToOperationName("Btn3")
    End Sub

    Private Sub AppendToOperationName(suffix As String)
        If _currentTechnology Is Nothing Then Exit Sub
        Try
            Dim param As EspritTechnology.IParameter = _currentTechnology.FindParameter("OperationName")
            Dim currentValue As String = CStr(param.Value)
            param.Value = currentValue & suffix
        Catch ex As Exception
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                "ContouringCompChecker",
                $"AppendToOperationName error: {ex.Message}")
        End Try
    End Sub

End Class
