Imports ESPRIT.NetApi.Ribbon

''' <summary>
''' Feature: Close All Open Edges
''' Iterates through selected FeatureChain items and sets the Feature_OpenEdge
''' custom property to False on every eligible sub-item.
''' </summary>
Public Class CloseAllOpenEdgeFeature
    Implements IFeature

    Private Const RIBBON_GROUP_KEY As String = "CloseAllOpenEdge_Group"
    Private Const RIBBON_BUTTON_KEY As String = "CloseAllOpenEdge_Btn"

    Private ReadOnly _app As ESPRIT.Application

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Dim group As IRibbonGroup = tab.Groups.Add(RIBBON_GROUP_KEY, "Edges")
        group.Items.AddButton(RIBBON_BUTTON_KEY, "Close Open Edges", True, LoadIcon("CloseFeatureEdge.ico"))
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        If e.Key = RIBBON_BUTTON_KEY Then
            e.Handled = True
            CloseAllOpenEdges()
            Return True
        End If
        Return False
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        ' No per-feature cleanup needed; the tab is removed by Main.
    End Sub

    Private Sub CloseAllOpenEdges()
        Dim doc As ESPRIT.Document = _app.Document

        If doc Is Nothing Then
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                "CloseAllOpenEdge",
                "No document is open.")
            Return
        End If

        If doc.Group.Count = 0 Then
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                "CloseAllOpenEdge",
                "No item selected. Please select features before running the extension.")
            Return
        End If

        Dim featureCount As Integer = 0
        Dim closedCount As Integer = 0
        Dim errorCount As Integer = 0

        For i As Integer = 1 To doc.Group.Count
            Dim item As ESPRIT.IGraphicObject = doc.Group.Item(i)

            If item.GraphicObjectType = EspritConstants.espGraphicObjectType.espFeatureChain Then
                featureCount += 1
                ' Get the ComFeature (EspritFeatures.ComFeature) via late binding
                Dim comFeature As Object = item.ComGraphicObject

                For j As Integer = 1 To comFeature.Count
                    Try
                        Dim subItem As Object = comFeature.Item(j)
                        Dim custProps As EspritProperties.ICustomProperties = subItem.CustomProperties
                        Dim custProp As EspritProperties.ICustomProperty = custProps.Item("Feature_OpenEdge")
                        If CBool(custProp.Value) = True Then
                            custProp.Value = False
                            closedCount += 1
                        End If
                    Catch
                        ' Sub-item does not have the Feature_OpenEdge property
                        errorCount += 1
                    End Try
                Next

                ' Workaround: ESPRIT EDGE does not refresh the UI after custom property changes.
                ' A double Reverse() call (net no-op) forces the display to update.
                Dim featureChain As Object = item
                featureChain.Reverse()
                featureChain.Reverse()
            End If
        Next

        If featureCount = 0 Then
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                "CloseAllOpenEdge",
                "No FeatureChain found in the selection.")
        Else

            Dim message As String = $"Done: {featureCount} feature(s) processed, {closedCount} edge(s) closed."
            If errorCount > 0 Then
                message &= $" ({errorCount} sub-item(s) without OpenEdge property were skipped.)"
            End If
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeInformation,
                "CloseAllOpenEdge",
                message)
        End If
    End Sub

End Class
