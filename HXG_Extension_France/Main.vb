Imports System.ComponentModel.Composition
Imports ESPRIT.NetApi.Extensions
Imports ESPRIT.NetApi.Ribbon

' To debug the extension:
' 1) In project properties > Build tab, set output folder to:
'    C:\Users\Public\Documents\Hexagon\ESPRIT EDGE\Data\Extensions\HXG_Extension_France\
' 2) In Debug tab, set "Start External Program" to the ESPRITEDGE.exe path.
' 3) In Debug tab, set "Working Directory" to the folder containing ESPRITEDGE.exe.
' Press F5 to start debugging.

' This value must match the major build number of the ESPRIT EDGE version you are targeting.
<Export(GetType(IExtension))>
<ExportMetadata("SupportBuild", 20)>
Public Class Main
    Implements IExtension

    Private Const RIBBON_TAB_KEY As String = "HXG_Extension_France_Tab"

    Public ReadOnly Property Description() As String = "HXG Extension France - ESPRIT EDGE productivity tools" Implements IBaseExtension.Description
    Public ReadOnly Property Name() As String = "HXG_Extension_France" Implements IBaseExtension.Name
    Public ReadOnly Property Publisher() As String = "Hexagon PS France - Todesco Andy" Implements IBaseExtension.Publisher
    Public ReadOnly Property Url() As String = "http://www.espritcam.com" Implements IBaseExtension.Url

    Private _espritApplication As ESPRIT.Application
    Private _features As New List(Of IFeature)

    Public Sub Connect(app As Object) Implements IExtension.Connect
        _espritApplication = DirectCast(app, ESPRIT.Application)
        ExtensionUtilities.Initialize(_espritApplication)
        Strings.SetCulture(CInt(_espritApplication.Lcid))
        SetupRibbon()
    End Sub

    Public Sub Disconnect() Implements IBaseExtension.Disconnect
        Try
            Dim ribbon As IRibbon = DirectCast(_espritApplication.Ribbon, IRibbon)
            RemoveHandler ribbon.OnButtonClick, AddressOf OnRibbonButtonClick
            For Each feature As IFeature In _features
                feature.Disconnect()
            Next
            If ribbon.Tabs.Contains(RIBBON_TAB_KEY) Then
                ribbon.Tabs.Remove(RIBBON_TAB_KEY)
            End If
        Catch
            ' Ignore cleanup errors
        End Try
    End Sub

    Private Sub SetupRibbon()
        Dim ribbon As IRibbon = _espritApplication.Ribbon
        Dim tab As IRibbonTab = ribbon.Tabs.Add(RIBBON_TAB_KEY, Strings.Tab_Title)

        ' --- Register features here ---

        _features.Add(New AlignTurningFeature(_espritApplication))
        _features.Add(New AlignMillingFeature(_espritApplication))
        _features.Add(New CloseAllOpenEdgeFeature(_espritApplication))
        _features.Add(New ContouringCompCheckerFeature(_espritApplication))
        _features.Add(New ChannelTimelineFeature(_espritApplication))

        '_features.Add(New DetectAndAlignPartFeature(_espritApplication))
        '_features.Add(New DebugExploration(_espritApplication))
        '_features.Add(New AddParkingOperationFeature(_espritApplication))
        '_features.Add(New SetCollinearAxisPositionFeature(_espritApplication))

        ' Initialize each feature's ribbon UI
        For Each feature As IFeature In _features
            feature.Setup(tab)
        Next

        ' Load localized tooltips after all buttons are created.
        Dim extDir As String = IO.Path.GetDirectoryName(Reflection.Assembly.GetExecutingAssembly().Location)
        Dim localizedFile As String = IO.Path.Combine(extDir, "Localization", $"HXG_Tooltips.{_espritApplication.Lcid}.xml")
        Dim defaultFile As String = IO.Path.Combine(extDir, "Localization", "HXG_Tooltips.xml")
        If IO.File.Exists(localizedFile) Then
            ribbon.LoadToolTipConfiguration(localizedFile)
        ElseIf IO.File.Exists(defaultFile) Then
            ribbon.LoadToolTipConfiguration(defaultFile)
        End If

        AddHandler ribbon.OnButtonClick, AddressOf OnRibbonButtonClick
    End Sub

    Private Sub OnRibbonButtonClick(sender As Object, e As ButtonClickEventArgs)
        For Each feature As IFeature In _features
            If feature.HandleButtonClick(e) Then Exit For
        Next
    End Sub

End Class
