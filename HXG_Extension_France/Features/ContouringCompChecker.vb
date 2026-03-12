Imports ESPRIT.NetApi.Ribbon

''' <summary>
''' Feature: Contouring Compensation Checker
''' Adds a group to the "ContextTab.CreateTechPage" ribbon tab.
''' Applies compensation presets and validates lead-in/out consistency.
''' </summary>
Public Class ContouringCompCheckerFeature
    Implements IFeature

    Private Const TARGET_TAB_KEY As String = "ContextTab.CreateTechPage"
    Private Const RIBBON_GROUP_KEY As String = "ContouringCompChecker_Group"
    Private Const RIBBON_SPLITBTN_KEY As String = "ContouringCompChecker_SplitBtn"
    Private Const RIBBON_BTN1_KEY As String = "ContouringCompChecker_Btn1"
    Private Const RIBBON_BTN2_KEY As String = "ContouringCompChecker_Btn2"
    Private Const RIBBON_BTN3_KEY As String = "ContouringCompChecker_Btn3"

    Private Const SOURCE As String = "ContouringCompChecker"

    Private ReadOnly _app As ESPRIT.Application
    Private _doc As ESPRIT.Document
    Private _splitButton As IRibbonSplitButton
    Private _techUtil As EspritTechnology.TechnologyUtility
    Private _currentTechnology As EspritTechnology.ITechnology

    Public Sub New(app As ESPRIT.Application)
        _app = app
        _doc = _app.Document
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
                New RibbonButtonInfo() With {.Key = RIBBON_BTN1_KEY, .Caption = "Set Profile Compensation", .Icon = LoadIcon("ProfilComp.ico")},
                New RibbonButtonInfo() With {.Key = RIBBON_BTN2_KEY, .Caption = "Set Center Compensation", .Icon = LoadIcon("ToolComp.ico")},
                New RibbonButtonInfo() With {.Key = RIBBON_BTN3_KEY, .Caption = "No Compensation", .Icon = LoadIcon("NoComp.ico")}
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
                OnSetProfileCompensation()
                Return True
            Case RIBBON_BTN2_KEY
                e.Handled = True
                OnSetCenterCompensation()
                Return True
            Case RIBBON_BTN3_KEY
                e.Handled = True
                OnSetNoCompensation()
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

    ' ── Tech page event ────────────────────────────────────────────────────────

    Private Sub OnTechPageUiLoaded(ByVal Technology As Object)
        If _splitButton Is Nothing Then Exit Sub
        Try
            Dim tech As EspritTechnology.ITechnology = CType(Technology, EspritTechnology.ITechnology)
            _currentTechnology = tech
            _splitButton.Visible = tech.HasClCode(245) AndAlso tech.HasClCode(780)
        Catch ex As Exception
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                SOURCE,
                $"OnTechPageUiLoaded error: {ex.Message}")
        End Try
    End Sub

    ' ── Core logic ────────────────────────────────────────────────────────────

    ''' <summary>Set Profile Compensation: CL245=Left, CL780=0 (profile — ESPRIT computes on profile, machine applies G41/G42)</summary>
    Private Sub OnSetProfileCompensation()
        SetCompensationParameters(EspritConstants.espOffsetSide.espOffsetLeft, False)
        ValidateLeadInOut()
    End Sub

    ''' <summary>Set Center Compensation: CL245=Left, CL780=1 (tool axis — ESPRIT offsets to tool center, machine applies G41/G42)</summary>
    Private Sub OnSetCenterCompensation()
        SetCompensationParameters(EspritConstants.espOffsetSide.espOffsetLeft, True)
        ValidateLeadInOut()
    End Sub

    ''' <summary>No Compensation: CL245=Off, CL780=1 (tool axis — no G41/G42, ESPRIT outputs tool center coordinates)</summary>
    Private Sub OnSetNoCompensation()
        SetCompensationParameters(EspritConstants.espOffsetSide.espOffsetOff, True)
    End Sub

    Private Sub SetCompensationParameters(cutterCompNC As EspritConstants.espOffsetSide, offsetToolRadius As Boolean)
        If _currentTechnology Is Nothing Then Exit Sub
        Try

            Dim paramComp As EspritTechnology.IParameter = _currentTechnology.FindParameter("CutterCompNC")
            paramComp.Value = cutterCompNC

            Dim paramOffset As EspritTechnology.IParameter = _currentTechnology.FindParameter("OffsetToolRadius")
            paramOffset.Source = EspritConstants.espTechItemSourceType.espTechItemSourceUser
            paramOffset.Value = offsetToolRadius

        Catch ex As Exception
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                SOURCE,
                $"SetCompensationParameters error: {ex.Message}")
        End Try
    End Sub

    ' ── Lead-in/out validation ─────────────────────────────────────────────────

    Private Sub ValidateLeadInOut()
        If _currentTechnology Is Nothing Then Exit Sub
        Try
            Dim offsetRegister As Double = 0
            Try
                Dim paramReg As EspritTechnology.IParameter = _currentTechnology.FindParameter("OffsetRegisterValue")
                offsetRegister = CDbl(paramReg.Value)
            Catch
                ' If OffsetRegisterValue is not available, threshold stays 0
            End Try

            If ParameterExists("LeadInType") Then
                ValidateLeadDirection(True, offsetRegister)
                ValidateLeadDirection(False, offsetRegister)
            End If
            If ParameterExists("FinishLeadInOutType") Then
                ValidateFinishLeadInOut(offsetRegister)
            End If
            If ParameterExists("OpenPocketLeadInOutType") Then
                ValidateOpenPocketLeadInOut(offsetRegister)
            End If
        Catch ex As Exception
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                SOURCE,
                $"ValidateLeadInOut error: {ex.Message}")
        End Try
    End Sub

    Private Sub ValidateLeadDirection(isLeadIn As Boolean, offsetRegister As Double)
        Dim typeParamName As String = If(isLeadIn, "LeadInType", "LeadOutType")
        Dim distanceParamName As String = If(isLeadIn, "LeadInDistance", "LeadOutDistance")
        Dim radiusParamName As String = If(isLeadIn, "LeadInRadius", "LeadOutRadius")
        Dim normalParamName As String = If(isLeadIn, "NormalLeadInDistance", "NormalLeadOutDistance")
        Dim xOffsetParamName As String = If(isLeadIn, "XOffsetLeadInDistance", "XOffsetLeadOutDistance")
        Dim yOffsetParamName As String = If(isLeadIn, "YOffsetLeadInDistance", "YOffsetLeadOutDistance")
        Dim dirLabel As String = If(isLeadIn, "Lead-in", "Lead-out")
        Try
            Dim typeParam As EspritTechnology.IParameter = _currentTechnology.FindParameter(typeParamName)
            If typeParam Is Nothing OrElse typeParam.Hidden Then Exit Sub
            Dim typeValue As Integer = CInt(typeParam.Value)

            If isLeadIn Then
                If typeValue = CInt(EspritConstants.espMillLeadinType.espMillLeadinTangent) Then
                    _app.EventWindow.AddMessage(EspritConstants.espMessageType.espMessageTypeWarning, SOURCE,
                        $"{dirLabel}: Tangent type is incompatible with compensation — a perpendicular lead-in is required.")
                    Exit Sub
                End If
                Select Case typeValue
                    Case CInt(EspritConstants.espMillLeadinType.espMillLeadinDistance)
                        CheckDistanceParam(distanceParamName, offsetRegister, dirLabel)
                    Case CInt(EspritConstants.espMillLeadinType.espMillLeadinRadius),
                         CInt(EspritConstants.espMillLeadinType.espMillLeadinRadiusNormal),
                         CInt(EspritConstants.espMillLeadinType.espMillLeadinPositionRadius),
                         CInt(EspritConstants.espMillLeadinType.espMillLeadinPositionRadiusNormal),
                         CInt(EspritConstants.espMillLeadinType.espMillLeadinRadiusAtAngle)
                        CheckDistanceParam(radiusParamName, offsetRegister, dirLabel)
                    Case CInt(EspritConstants.espMillLeadinType.espMillLeadinNormalTangent)
                        CheckDistanceParam(normalParamName, offsetRegister, dirLabel)
                    Case CInt(EspritConstants.espMillLeadinType.espMillLeadinXOffset)
                        CheckDistanceParam(xOffsetParamName, offsetRegister, dirLabel)
                    Case CInt(EspritConstants.espMillLeadinType.espMillLeadinYOffset)
                        CheckDistanceParam(yOffsetParamName, offsetRegister, dirLabel)
                End Select
            Else
                If typeValue = CInt(EspritConstants.espMillLeadoutType.espMillLeadoutTangent) Then
                    _app.EventWindow.AddMessage(EspritConstants.espMessageType.espMessageTypeWarning, SOURCE,
                        $"{dirLabel}: Tangent type is incompatible with compensation — a perpendicular lead-out is required.")
                    Exit Sub
                End If
                Select Case typeValue
                    Case CInt(EspritConstants.espMillLeadoutType.espMillLeadoutDistance)
                        CheckDistanceParam(distanceParamName, offsetRegister, dirLabel)
                    Case CInt(EspritConstants.espMillLeadoutType.espMillLeadoutRadius),
                         CInt(EspritConstants.espMillLeadoutType.espMillLeadoutRadiusNormal),
                         CInt(EspritConstants.espMillLeadoutType.espMillLeadoutPositionRadius),
                         CInt(EspritConstants.espMillLeadoutType.espMillLeadoutPositionRadiusNormal),
                         CInt(EspritConstants.espMillLeadoutType.espMillLeadoutRadiusAtAngle)
                        CheckDistanceParam(radiusParamName, offsetRegister, dirLabel)
                    Case CInt(EspritConstants.espMillLeadoutType.espMillLeadoutNormalTangent)
                        CheckDistanceParam(normalParamName, offsetRegister, dirLabel)
                    Case CInt(EspritConstants.espMillLeadoutType.espMillLeadoutXOffset)
                        CheckDistanceParam(xOffsetParamName, offsetRegister, dirLabel)
                    Case CInt(EspritConstants.espMillLeadoutType.espMillLeadoutYOffset)
                        CheckDistanceParam(yOffsetParamName, offsetRegister, dirLabel)
                End Select
            End If
        Catch
            ' Parameter not present in this operation type — skip
        End Try
    End Sub

    Private Sub CheckDistanceParam(paramName As String, offsetRegister As Double, dirLabel As String)
        Try
            Dim param As EspritTechnology.IParameter = _currentTechnology.FindParameter(paramName)
            If param Is Nothing OrElse param.Hidden Then Exit Sub
            Dim value As Double = CDbl(param.Value)
            If value <= offsetRegister Then
                _app.EventWindow.AddMessage(
                    EspritConstants.espMessageType.espMessageTypeWarning,
                    SOURCE,
                    $"{dirLabel}: {CStr(param.Caption)} = {value} must be greater than OffsetRegisterValue ({offsetRegister}).")
            End If
        Catch
            ' Parameter not present in this operation type — skip
        End Try
    End Sub

    Private Sub ValidateFinishLeadInOut(offsetRegister As Double)
        Const dirLabel As String = "Finish Lead-in/out"
        Try
            Dim typeParam As EspritTechnology.IParameter = _currentTechnology.FindParameter("FinishLeadInOutType")
            If typeParam Is Nothing OrElse typeParam.Hidden Then Exit Sub
            Dim typeValue As Integer = CInt(typeParam.Value)
            Select Case typeValue
                Case CInt(EspritConstants.espPocketFinishLeadInOut.espPocketFinishLeadInOutDistance)
                    CheckDistanceParam("FinishLeadInOutDistance", offsetRegister, dirLabel)
                Case CInt(EspritConstants.espPocketFinishLeadInOut.espPocketFinishLeadInOutRadius),
                     CInt(EspritConstants.espPocketFinishLeadInOut.espPocketFinishLeadInOutRadiusNormal)
                    CheckDistanceParam("FinishLeadInOutRadius", offsetRegister, dirLabel)
            End Select
        Catch
        End Try
    End Sub

    Private Sub ValidateOpenPocketLeadInOut(offsetRegister As Double)
        Const dirLabel As String = "Open Pocket Lead-in/out"
        Try
            Dim typeParam As EspritTechnology.IParameter = _currentTechnology.FindParameter("OpenPocketLeadInOutType")
            If typeParam Is Nothing OrElse typeParam.Hidden Then Exit Sub
            Dim typeValue As Integer = CInt(typeParam.Value)
            If typeValue = CInt(EspritConstants.espOpenPocketLeadInOutType.espOpenPocketLeadInOutTangent) Then
                _app.EventWindow.AddMessage(EspritConstants.espMessageType.espMessageTypeWarning, SOURCE,
                    $"{dirLabel}: Tangent type is incompatible with compensation — a perpendicular lead-in/out is required.")
                Exit Sub
            End If
            Select Case typeValue
                Case CInt(EspritConstants.espOpenPocketLeadInOutType.espOpenPocketLeadInOutDistance)
                    CheckDistanceParam("OpenPocketLeadInOutTangentDistance", offsetRegister, dirLabel)
                Case CInt(EspritConstants.espOpenPocketLeadInOutType.espOpenPocketLeadInOutTangentNormal)
                    CheckDistanceParam("OpenPocketLeadInOutNormalDistance", offsetRegister, dirLabel)
            End Select
        Catch
        End Try
    End Sub

    Private Function ParameterExists(paramName As String) As Boolean
        Try
            Return _currentTechnology.FindParameter(paramName) IsNot Nothing
        Catch
            Return False
        End Try
    End Function

End Class
