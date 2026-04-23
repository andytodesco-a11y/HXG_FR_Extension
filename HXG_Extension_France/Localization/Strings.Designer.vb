Imports System.Globalization
Imports System.Resources

''' <summary>
''' Strongly-typed accessor for localised UI strings.
''' Default culture: English (EN). French (FR) is loaded automatically when
''' ESPRIT is configured in French (Lcid = 1036).
'''
''' To add a new string:
'''   1. Add a &lt;data&gt; entry to Strings.resx (English value).
'''   2. Add the same key to Strings.fr.resx (French value).
'''   3. Add a ReadOnly Property here.
''' </summary>
Friend Module Strings

    Private ReadOnly _rm As New ResourceManager(
        "HXG_Extension_France.Strings",
        System.Reflection.Assembly.GetExecutingAssembly())

    Private _culture As CultureInfo = Nothing

    ''' <summary>
    ''' Sets the active culture from an ESPRIT LCID (e.g. 1036 = fr-FR).
    ''' Call once from Main.Connect() using _espritApplication.Lcid.
    ''' Falls back to English if the LCID has no matching satellite assembly.
    ''' </summary>
    Friend Sub SetCulture(lcid As Integer)
        Try
            _culture = CultureInfo.GetCultureInfo(lcid)
        Catch
            _culture = Nothing
        End Try
    End Sub

    Private Function S(key As String) As String
        Return _rm.GetString(key, _culture)
    End Function

    ' ── General ──────────────────────────────────────────────────────────────

    Friend ReadOnly Property Msg_NoDocument As String
        Get
            Return S("Msg_NoDocument")
        End Get
    End Property

    ' ── Main ─────────────────────────────────────────────────────────────────

    Friend ReadOnly Property Tab_Title As String
        Get
            Return S("Tab_Title")
        End Get
    End Property

    ' ── CloseAllOpenEdge ──────────────────────────────────────────────────────

    Friend ReadOnly Property CloseEdge_GroupLabel As String
        Get
            Return S("CloseEdge_GroupLabel")
        End Get
    End Property

    Friend ReadOnly Property CloseEdge_ButtonLabel As String
        Get
            Return S("CloseEdge_ButtonLabel")
        End Get
    End Property

    Friend ReadOnly Property CloseEdge_NoSelection As String
        Get
            Return S("CloseEdge_NoSelection")
        End Get
    End Property

    Friend ReadOnly Property CloseEdge_NoFeatureChain As String
        Get
            Return S("CloseEdge_NoFeatureChain")
        End Get
    End Property

    Friend ReadOnly Property CloseEdge_Done As String
        Get
            Return S("CloseEdge_Done")
        End Get
    End Property

    Friend ReadOnly Property CloseEdge_Skipped As String
        Get
            Return S("CloseEdge_Skipped")
        End Get
    End Property

    ' ── AlignTurning ─────────────────────────────────────────────────────────

    Friend ReadOnly Property AlignPart_GroupLabel As String
        Get
            Return S("AlignPart_GroupLabel")
        End Get
    End Property

    Friend ReadOnly Property AlignOptions_GroupLabel As String
        Get
            Return S("AlignOptions_GroupLabel")
        End Get
    End Property

    Friend ReadOnly Property AlignTurning_ButtonLabel As String
        Get
            Return S("AlignTurning_ButtonLabel")
        End Get
    End Property

    Friend ReadOnly Property AlignTurning_FlipButtonLabel As String
        Get
            Return S("AlignTurning_FlipButtonLabel")
        End Get
    End Property

    Friend ReadOnly Property AlignTurning_NoAxis As String
        Get
            Return S("AlignTurning_NoAxis")
        End Get
    End Property

    Friend ReadOnly Property AlignTurning_Done As String
        Get
            Return S("AlignTurning_Done")
        End Get
    End Property

    Friend ReadOnly Property AlignTurning_FlipDone As String
        Get
            Return S("AlignTurning_FlipDone")
        End Get
    End Property

    ' ── AlignMilling ─────────────────────────────────────────────────────────

    Friend ReadOnly Property AlignMilling_ButtonLabel As String
        Get
            Return S("AlignMilling_ButtonLabel")
        End Get
    End Property

    Friend ReadOnly Property AlignX_ButtonLabel As String
        Get
            Return S("AlignX_ButtonLabel")
        End Get
    End Property

    Friend ReadOnly Property OriginPosition_ButtonLabel As String
        Get
            Return S("OriginPosition_ButtonLabel")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_NoFaces As String
        Get
            Return S("AlignMilling_NoFaces")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_NoDominantAxis As String
        Get
            Return S("AlignMilling_NoDominantAxis")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_NoTopFace As String
        Get
            Return S("AlignMilling_NoTopFace")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_Done As String
        Get
            Return S("AlignMilling_Done")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_OrientXYNoData As String
        Get
            Return S("AlignMilling_OrientXYNoData")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_OrientXYOptimal As String
        Get
            Return S("AlignMilling_OrientXYOptimal")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_OrientXYRotated As String
        Get
            Return S("AlignMilling_OrientXYRotated")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_P0Error As String
        Get
            Return S("AlignMilling_P0Error")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_OriginSet As String
        Get
            Return S("AlignMilling_OriginSet")
        End Get
    End Property

    Friend ReadOnly Property AlignMilling_OriginError As String
        Get
            Return S("AlignMilling_OriginError")
        End Get
    End Property

    ' ── BoundingBoxOriginDialog ───────────────────────────────────────────────

    Friend ReadOnly Property BBoxDlg_Title As String
        Get
            Return S("BBoxDlg_Title")
        End Get
    End Property

    Friend ReadOnly Property BBoxDlg_XYPosition As String
        Get
            Return S("BBoxDlg_XYPosition")
        End Get
    End Property

    Friend ReadOnly Property BBoxDlg_ZPosition As String
        Get
            Return S("BBoxDlg_ZPosition")
        End Get
    End Property

    Friend ReadOnly Property BBoxDlg_ZPlus As String
        Get
            Return S("BBoxDlg_ZPlus")
        End Get
    End Property

    Friend ReadOnly Property BBoxDlg_ZCenter As String
        Get
            Return S("BBoxDlg_ZCenter")
        End Get
    End Property

    Friend ReadOnly Property BBoxDlg_ZMinus As String
        Get
            Return S("BBoxDlg_ZMinus")
        End Get
    End Property

    Friend ReadOnly Property BBoxDlg_OK As String
        Get
            Return S("BBoxDlg_OK")
        End Get
    End Property

    Friend ReadOnly Property BBoxDlg_Cancel As String
        Get
            Return S("BBoxDlg_Cancel")
        End Get
    End Property

    ' ── ContouringCompChecker ─────────────────────────────────────────────────

    Friend ReadOnly Property CompChecker_GroupLabel As String
        Get
            Return S("CompChecker_GroupLabel")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_ProfileBtnLabel As String
        Get
            Return S("CompChecker_ProfileBtnLabel")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_CenterBtnLabel As String
        Get
            Return S("CompChecker_CenterBtnLabel")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_NoCompBtnLabel As String
        Get
            Return S("CompChecker_NoCompBtnLabel")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_LeadIn As String
        Get
            Return S("CompChecker_LeadIn")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_LeadOut As String
        Get
            Return S("CompChecker_LeadOut")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_FinishLeadInOut As String
        Get
            Return S("CompChecker_FinishLeadInOut")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_OpenPocketLeadInOut As String
        Get
            Return S("CompChecker_OpenPocketLeadInOut")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_TangentLeadIn As String
        Get
            Return S("CompChecker_TangentLeadIn")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_TangentLeadOut As String
        Get
            Return S("CompChecker_TangentLeadOut")
        End Get
    End Property

    Friend ReadOnly Property CompChecker_DistanceTooSmall As String
        Get
            Return S("CompChecker_DistanceTooSmall")
        End Get
    End Property

    ' ── ExtensionUtilities / AlignX ───────────────────────────────────────────

    Friend ReadOnly Property Selection_NoElement As String
        Get
            Return S("Selection_NoElement")
        End Get
    End Property

    Friend ReadOnly Property Selection_MultipleElements As String
        Get
            Return S("Selection_MultipleElements")
        End Get
    End Property

    Friend ReadOnly Property Selection_NotASolid As String
        Get
            Return S("Selection_NotASolid")
        End Get
    End Property

    Friend ReadOnly Property AlignX_NoSelection As String
        Get
            Return S("AlignX_NoSelection")
        End Get
    End Property

    Friend ReadOnly Property AlignX_UnsupportedEdge As String
        Get
            Return S("AlignX_UnsupportedEdge")
        End Get
    End Property

    Friend ReadOnly Property AlignX_NoAngle As String
        Get
            Return S("AlignX_NoAngle")
        End Get
    End Property

    Friend ReadOnly Property AlignX_Rotated As String
        Get
            Return S("AlignX_Rotated")
        End Get
    End Property

    ' ── ChannelTimeline ───────────────────────────────────────────────────────

    Friend ReadOnly Property Timeline_GroupLabel As String
        Get
            Return S("Timeline_GroupLabel")
        End Get
    End Property

    Friend ReadOnly Property Timeline_ButtonLabel As String
        Get
            Return S("Timeline_ButtonLabel")
        End Get
    End Property

    Friend ReadOnly Property Timeline_FormTitle As String
        Get
            Return S("Timeline_FormTitle")
        End Get
    End Property

    Friend ReadOnly Property Timeline_RefreshButton As String
        Get
            Return S("Timeline_RefreshButton")
        End Get
    End Property

    Friend ReadOnly Property Timeline_NoProgram As String
        Get
            Return S("Timeline_NoProgram")
        End Get
    End Property

    Friend ReadOnly Property Timeline_TotalTime As String
        Get
            Return S("Timeline_TotalTime")
        End Get
    End Property

    Friend ReadOnly Property Timeline_Channels As String
        Get
            Return S("Timeline_Channels")
        End Get
    End Property

    Friend ReadOnly Property Timeline_ChannelEmpty As String
        Get
            Return S("Timeline_ChannelEmpty")
        End Get
    End Property

    Friend ReadOnly Property Timeline_Legend_MachineOp_Turning As String
        Get
            Return S("Timeline_Legend_MachineOp_Turning")
        End Get
    End Property

    Friend ReadOnly Property Timeline_Legend_MachineOp_Milling As String
        Get
            Return S("Timeline_Legend_MachineOp_Milling")
        End Get
    End Property

    Friend ReadOnly Property Timeline_Legend_MachineOp_Other As String
        Get
            Return S("Timeline_Legend_MachineOp_Other")
        End Get
    End Property

    Friend ReadOnly Property Timeline_Legend_ToolPath As String
        Get
            Return S("Timeline_Legend_ToolPath")
        End Get
    End Property

    Friend ReadOnly Property Timeline_Legend_ToolChange As String
        Get
            Return S("Timeline_Legend_ToolChange")
        End Get
    End Property

    Friend ReadOnly Property Timeline_Legend_SetupChange As String
        Get
            Return S("Timeline_Legend_SetupChange")
        End Get
    End Property

    Friend ReadOnly Property Timeline_Legend_Link As String
        Get
            Return S("Timeline_Legend_Link")
        End Get
    End Property

    Friend ReadOnly Property Timeline_Legend_Sync As String
        Get
            Return S("Timeline_Legend_Sync")
        End Get
    End Property

End Module
