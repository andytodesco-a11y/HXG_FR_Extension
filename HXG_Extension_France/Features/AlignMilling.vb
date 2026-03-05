Imports ESPRIT.NetApi.Ribbon
Imports EspritGeometryBase

''' <summary>
''' Feature: Milling Alignment
''' Provides step-by-step milling part alignment tools:
'''   - Align Part  : aligns the selected solid along Z using the largest planar face,
'''                   then moves P0 to the highest Z point.
'''   - Move Origin : (placeholder)
''' </summary>
Public Class AlignMillingFeature
    Implements IFeature

    ' ── Ribbon keys ──────────────────────────────────────────────────────────
    Private Const RIBBON_GROUP_KEY       As String = "AlignMilling_Group"
    Private Const BTN_ALIGN_KEY          As String = "AlignMilling_Align_Btn"
    Private Const BTN_MOVE_ORIGIN_KEY    As String = "AlignMilling_MoveOrigin_Btn"

    Private Const LOG_SOURCE As String = "MillingAlignment"

    Private ReadOnly _app As ESPRIT.Application

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    ' ── IFeature ─────────────────────────────────────────────────────────────

    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Dim icon As System.Drawing.Icon = LoadIcon()
        Dim group As IRibbonGroup = tab.Groups.Add(RIBBON_GROUP_KEY, "Milling Alignment")
        group.Items.AddButton(BTN_ALIGN_KEY,       "Align Part",  True, icon)
        group.Items.AddButton(BTN_MOVE_ORIGIN_KEY, "Move Origin", True, icon)
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        Select Case e.Key
            Case BTN_ALIGN_KEY
                e.Handled = True
                AlignPart()
                Return True
            Case BTN_MOVE_ORIGIN_KEY
                e.Handled = True
                LogInfo("Move Origin: not implemented yet.")
                Return True
        End Select
        Return False
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        ' No per-feature cleanup needed; the tab is removed by Main.
    End Sub

    ' ── Actions ──────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Finds the largest planar face of the selected solid, aligns it along Z,
    ''' then moves P0 to the highest Z point of the bounding box.
    ''' </summary>
    Private Sub AlignPart()
        Dim doc As ESPRIT.Document = _app.Document
        If doc Is Nothing Then LogWarning("No document is open.") : Return

        Dim solid As ESPRIT.ISolid = GetSelectedSolid(doc)
        If solid Is Nothing Then Return

        Dim body  As EspritSolids.ISolidBody  = CType(solid.SolidBody, EspritSolids.ISolidBody)
        Dim faces As EspritSolids.ISolidFaces = body.SolidFaces

        Dim alignmentFace As EspritSolids.ISolidFace = GetLargestPlanarFace(faces)
        If alignmentFace Is Nothing Then alignmentFace = faces.Item(1)

        solid.Grouped = False
        doc.Group.Add(alignmentFace)
        doc.AlignAlongAxis("Z")

        MoveP0ToTopZ(doc, solid)
        LogInfo("Milling part aligned along Z axis.")
    End Sub

    ' ── Solid selection ───────────────────────────────────────────────────────

    ''' <summary>
    ''' Returns the selected solid after validating that exactly one element is selected
    ''' and that it is an ISolid. Logs a specific warning and returns Nothing on failure.
    ''' </summary>
    Private Function GetSelectedSolid(doc As ESPRIT.Document) As ESPRIT.ISolid
        If doc.Group.Count = 0 Then
            LogWarning("No element selected. Please select a single solid.")
            Return Nothing
        End If
        If doc.Group.Count > 1 Then
            LogWarning("Multiple elements selected. Please select a single solid only.")
            Return Nothing
        End If
        Dim solid As ESPRIT.ISolid = TryCast(doc.Group.Item(1), ESPRIT.ISolid)
        If solid Is Nothing Then
            LogWarning("The selected element is not a solid.")
            Return Nothing
        End If
        Return solid
    End Function

    ' ── Face selection ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Returns the planar face with the largest UV parameter extent (proxy for
    ''' surface area). Returns Nothing if the solid has no planar face.
    ''' </summary>
    Private Function GetLargestPlanarFace(faces As EspritSolids.ISolidFaces) As EspritSolids.ISolidFace
        Dim best       As EspritSolids.ISolidFace = Nothing
        Dim bestExtent As Double = -1.0

        For i As Long = 1 To faces.Count
            Dim face As EspritSolids.ISolidFace    = faces.Item(i)
            Dim surf As EspritSolids.ISolidSurface = face.SolidSurface
            If surf.SurfaceType = EspritSolids.SolidSurfaceType.geoSurfacePlane Then
                Dim uMin As Double = 0, uMax As Double = 0
                Dim vMin As Double = 0, vMax As Double = 0
                surf.SurfaceLimits(uMin, uMax, vMin, vMax)
                Dim extent As Double = (uMax - uMin) * (vMax - vMin)
                If extent > bestExtent Then
                    bestExtent = extent
                    best       = face
                End If
            End If
        Next
        Return best
    End Function

    ' ── P0 placement ─────────────────────────────────────────────────────────

    ''' <summary>
    ''' Computes the bounding box of the solid body and moves P0 to (0, 0, targetZ).
    ''' By default targetZ = maxZ (top face). Pass toMinus:=True to use minZ instead.
    ''' </summary>
    Private Sub MoveP0ToTopZ(doc As ESPRIT.Document, solid As ESPRIT.ISolid,
                              Optional toMinus As Boolean = False)
        Try
            Dim body  As EspritSolids.ISolidBody      = CType(solid.SolidBody, EspritSolids.ISolidBody)
            Dim minPt As EspritGeometryBase.IComPoint = Nothing
            Dim maxPt As EspritGeometryBase.IComPoint = Nothing

            Dim matrix As EspritGeometryBase.IComMatrix =
                CType(doc.Planes.Item("XYZ").GlobalToLocalMatrix, EspritGeometryBase.IComMatrix)

            body.Box(matrix, minPt, maxPt)

            Dim targetZ As Double = If(toMinus, minPt.Z, maxPt.Z)
            Dim tempPoint As Object = doc.Points.Add(0.0, 0.0, targetZ)
            Try
                doc.MoveP0(tempPoint)
            Finally
                Try
                    doc.Points.Remove(CInt(doc.Points.IndexOf(tempPoint)))
                Catch
                End Try
            End Try
        Catch ex As Exception
            LogWarning($"Could not move P0: {ex.Message}")
        End Try
    End Sub

    ' ── Ribbon helpers ────────────────────────────────────────────────────────

    Private Function LoadIcon() As System.Drawing.Icon
        Try
            Dim assemblyDir As String = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location)
            Dim iconPath As String = System.IO.Path.Combine(assemblyDir, "HXG_Extension_France_Large.ico")
            If System.IO.File.Exists(iconPath) Then
                Return New System.Drawing.Icon(iconPath)
            End If
        Catch
        End Try
        Return Nothing
    End Function

    ' ── Logging ──────────────────────────────────────────────────────────────

    Private Sub LogInfo(msg As String)
        _app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeInformation, LOG_SOURCE, msg)
    End Sub

    Private Sub LogWarning(msg As String)
        _app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeWarning, LOG_SOURCE, msg)
    End Sub

End Class
