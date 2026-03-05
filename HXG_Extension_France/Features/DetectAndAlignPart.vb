Imports ESPRIT.NetApi.Ribbon
Imports EspritGeometryBase

''' <summary>
''' Feature: Detect And Align Part
''' Analyzes the solid in the current selection to determine whether it is a
''' turning or milling part, then aligns it along the Z axis.
'''   - Turning : revolution axis aligned to Z using a cylindrical/conical face.
'''   - Milling : largest flat face normal aligned to Z.
''' After alignment a direction check is performed; if the revolution axis points
''' toward -Z the part is flipped 180 degrees around the X axis (through its
''' center of gravity) via IManipulation.Rotate.
''' </summary>
Public Class DetectAndAlignPartFeature
    Implements IFeature

    Private Const RIBBON_GROUP_KEY As String = "DetectAndAlign_Group"
    Private Const RIBBON_BUTTON_KEY As String = "DetectAndAlign_Btn"
    Private Const LOG_SOURCE As String = "DetectAndAlign"
    Private Const TEMP_SS_NAME As String = "_HXG_TempFlip"

    ''' <summary>
    ''' Minimum ratio: dominant-cluster cylindrical faces / total faces.
    ''' Lowered to accommodate complex housings with many planar pocket faces.
    ''' </summary>
    Private Const TURNING_FACE_RATIO As Double = 0.1

    ''' <summary>
    ''' Minimum ratio: dominant-cluster cylindrical faces / all cylindrical faces.
    ''' Ensures the dominant axis accounts for a majority of the cylindrical geometry.
    ''' </summary>
    Private Const DOMINANT_CLUSTER_RATIO As Double = 0.5

    ''' <summary>
    ''' Minimum |dot product| for two unit axis vectors to be considered collinear.
    ''' Corresponds to a maximum angular deviation of ~10 degrees.
    ''' </summary>
    Private Const COLLINEARITY_THRESHOLD As Double = 0.985

    Private ReadOnly _app As ESPRIT.Application

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    ' ── IFeature ─────────────────────────────────────────────────────────────

    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Dim group As IRibbonGroup = tab.Groups.Add(RIBBON_GROUP_KEY, "Part Setup")
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
        group.Items.AddButton(RIBBON_BUTTON_KEY, "Detect && Align Part", True, icon)
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        If e.Key = RIBBON_BUTTON_KEY Then
            e.Handled = True
            DetectAndAlignPart()
            Return True
        End If
        Return False
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        ' No per-feature cleanup needed; the tab is removed by Main.
    End Sub

    ' ── Core logic ───────────────────────────────────────────────────────────

    Private Sub DetectAndAlignPart()
        Dim doc As ESPRIT.Document = _app.Document

        If doc Is Nothing Then
            LogWarning("No document is open.")
            Return
        End If

        Dim solid As ESPRIT.ISolid = GetSelectedSolid(doc)
        If solid Is Nothing Then
            LogWarning("No solid selected. Please select a solid before running.")
            Return
        End If

        Dim isTurning As Boolean
        Dim alignmentFace As EspritSolids.ISolidFace = Nothing
        AnalyzeSolid(solid, isTurning, alignmentFace)

        If alignmentFace Is Nothing Then
            LogWarning("Could not determine a suitable alignment face from the selected solid.")
            Return
        End If

        ' Align the representative face's principal axis to Z.
        ' AlignAlongAxis accepts both planar and cylindrical faces:
        '   - Planar face  : aligns the face normal to Z.
        '   - Cylinder/cone: aligns the revolution axis to Z.
        '
        ' Pattern from the official tutorial: ungroup the solid via its own
        ' Grouped property (NOT doc.Group.Clear), then add only the face.
        ' Using doc.Group.Clear() loses the document context and causes a
        ' ComException; solid.Grouped = False keeps the context intact.
        solid.Grouped = False
        doc.Group.Add(alignmentFace)
        doc.AlignAlongAxis("Z")

        ' For turning parts: verify the revolution axis points toward +Z and
        ' flip if necessary.
        If isTurning Then
            CheckAndFlipIfNeeded(doc, solid, alignmentFace)
        End If

        Dim partType As String = If(isTurning, "turning part", "milling part")
        LogInfo($"Detected as {partType} — aligned along Z axis.")

        ' Move P0 to the highest Z point of the aligned solid (Z only).
        MoveP0ToTopZ(doc, solid)
    End Sub

    ''' <summary>Returns the first ISolid found in the current selection group.</summary>
    Private Function GetSelectedSolid(doc As ESPRIT.Document) As ESPRIT.ISolid
        For i As Integer = 1 To doc.Group.Count
            Dim solid As ESPRIT.ISolid = TryCast(doc.Group.Item(i), ESPRIT.ISolid)
            If solid IsNot Nothing Then Return solid
        Next
        Return Nothing
    End Function

    ' ── Analysis ─────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Classifies the solid (turning vs milling) and outputs the best face to
    ''' pass to AlignAlongAxis("Z").
    '''
    ''' Turning detection algorithm:
    '''   1. Iterate all faces; for cylindrical/conical faces extract the axis
    '''      direction via the V-derivative of Evaluate() at the face midpoint.
    '''   2. Use majority voting (FindDominantAxisFace) to identify the dominant
    '''      axis cluster. Tolerates perpendicular holes/features that would break
    '''      a strict "all axes must be collinear" check.
    '''   3. isTurning when:
    '''        - dominant cluster >= DOMINANT_CLUSTER_RATIO of all cylinder axes
    '''        - revolution faces >= TURNING_FACE_RATIO of total faces
    '''      Representative face = the face whose axis leads the dominant cluster.
    '''
    ''' Milling fallback:
    '''   Representative face = planar face with largest UV parameter extent
    '''   (proxy for surface area). Falls back to face #1 if none found.
    ''' </summary>
    Private Sub AnalyzeSolid(solid As ESPRIT.ISolid,
                              ByRef isTurning As Boolean,
                              ByRef alignmentFace As EspritSolids.ISolidFace)
        isTurning = False
        alignmentFace = Nothing

        Dim body As EspritSolids.ISolidBody = CType(solid.SolidBody, EspritSolids.ISolidBody)
        Dim faces As EspritSolids.ISolidFaces = body.SolidFaces
        Dim totalFaces As Integer = CInt(faces.Count)

        Dim axisVectors As New List(Of Double())()
        Dim axisFaces As New List(Of EspritSolids.ISolidFace)()
        Dim revFaceCount As Integer = 0

        For i As Long = 1 To totalFaces
            Dim face As EspritSolids.ISolidFace = faces.Item(i)
            Dim surf As EspritSolids.ISolidSurface = face.SolidSurface

            Select Case surf.SurfaceType
                Case EspritSolids.SolidSurfaceType.geoSurfaceCylinder,
                     EspritSolids.SolidSurfaceType.geoSurfaceCone
                    revFaceCount += 1
                    Dim axis As Double() = ExtractAxisFromFace(surf)
                    If axis IsNot Nothing Then
                        axisVectors.Add(axis)
                        axisFaces.Add(face)
                    End If

                Case EspritSolids.SolidSurfaceType.geoSurfaceTorus,
                     EspritSolids.SolidSurfaceType.geoSurfaceSpun,
                     EspritSolids.SolidSurfaceType.geoSurfaceSphere
                    revFaceCount += 1
            End Select
        Next

        Dim revScore As Double = If(totalFaces > 0, CDbl(revFaceCount) / CDbl(totalFaces), 0.0)

        Dim dominantFace As EspritSolids.ISolidFace = Nothing
        Dim dominantVotes As Integer = 0
        If axisVectors.Count > 0 Then
            FindDominantAxisFace(axisVectors, axisFaces, dominantFace, dominantVotes)
        End If

        isTurning = (dominantVotes > 0) AndAlso
                    (CDbl(dominantVotes) / CDbl(axisVectors.Count) >= DOMINANT_CLUSTER_RATIO) AndAlso
                    (revScore >= TURNING_FACE_RATIO)

        LogInfo($"Analysis: {totalFaces} faces total, {revFaceCount} revolution, " &
                $"{axisVectors.Count} with axis ({dominantVotes} in dominant cluster) → " &
                $"{If(isTurning, "turning", "milling")}")

        If isTurning Then
            ' dominantFace is the largest cylinder in the dominant axis cluster.
            ' It may be Nothing if the cluster contains only conical faces (rare),
            ' because AlignAlongAxis does not accept cones. Fall back to a plane.
            alignmentFace = dominantFace
            If alignmentFace Is Nothing Then
                LogWarning("Turning part detected but no cylindrical face found — falling back to largest plane.")
                alignmentFace = GetLargestPlanarFace(faces)
                If alignmentFace Is Nothing Then alignmentFace = faces.Item(1)
            End If
        Else
            alignmentFace = GetLargestPlanarFace(faces)
            If alignmentFace Is Nothing Then alignmentFace = faces.Item(1)
        End If
    End Sub

    ''' <summary>
    ''' Evaluates the V-derivative of a cylindrical/conical surface at its UV
    ''' midpoint and returns the normalized axis direction vector (length 3 array).
    ''' Returns Nothing if the derivative length is negligible.
    ''' </summary>
    Private Function ExtractAxisFromFace(surf As EspritSolids.ISolidSurface) As Double()
        Dim uMin As Double = 0, uMax As Double = 0
        Dim vMin As Double = 0, vMax As Double = 0
        surf.SurfaceLimits(uMin, uMax, vMin, vMax)

        ' Evaluate at midpoint with 1 V-derivative (nUDeriv=0, nVDeriv=1).
        ' Result array (0-based): (0)=P(u,v)  (1)=dP/dV = axis direction.
        Dim res As Object = surf.Evaluate(
            (uMin + uMax) / 2.0,
            (vMin + vMax) / 2.0,
            0, 1)

        Dim deriv As IComPoint = CType(res(1), IComPoint)
        Dim length As Double = Math.Sqrt(deriv.X * deriv.X +
                                          deriv.Y * deriv.Y +
                                          deriv.Z * deriv.Z)
        If length < 0.000001 Then Return Nothing

        Return New Double() {deriv.X / length, deriv.Y / length, deriv.Z / length}
    End Function

    ''' <summary>
    ''' Finds the dominant axis cluster using majority voting (two passes).
    '''
    ''' Pass 1 — Vote: for each candidate axis, count how many other axes are
    '''   collinear (|dot| >= COLLINEARITY_THRESHOLD). The direction with the
    '''   most votes becomes the dominant axis reference.
    '''
    ''' Pass 2 — Best face: among all faces whose axis is collinear with the
    '''   dominant reference, select the one with the highest surface-area score:
    '''     score = |dP/dU| × uRange × vRange
    '''   where |dP/dU| equals the cylinder/cone radius at the evaluation point.
    '''   This ensures that large bores are always preferred over small drill
    '''   holes even when both share the same axis direction.
    ''' </summary>
    Private Sub FindDominantAxisFace(axisVectors As List(Of Double()),
                                      axisFaces As List(Of EspritSolids.ISolidFace),
                                      ByRef dominantFace As EspritSolids.ISolidFace,
                                      ByRef dominantVotes As Integer)
        dominantFace = Nothing
        dominantVotes = 0

        ' Pass 1: find the axis direction with the most collinear neighbours.
        Dim dominantRef As Double() = Nothing
        For i As Integer = 0 To axisVectors.Count - 1
            Dim ref As Double() = axisVectors(i)
            Dim votes As Integer = 0
            For j As Integer = 0 To axisVectors.Count - 1
                Dim dot As Double = ref(0) * axisVectors(j)(0) +
                                    ref(1) * axisVectors(j)(1) +
                                    ref(2) * axisVectors(j)(2)
                If Math.Abs(dot) >= COLLINEARITY_THRESHOLD Then votes += 1
            Next
            If votes > dominantVotes Then
                dominantVotes = votes
                dominantRef = ref
            End If
        Next

        If dominantRef Is Nothing Then Return

        ' Pass 2: among faces in the dominant cluster, pick the largest CYLINDER.
        ' AlignAlongAxis only accepts geoSurfaceCylinder and geoSurfacePlane —
        ' conical faces are valid for classification (Pass 1) but NOT for alignment.
        ' Score = radius × uRange × vRange  (proportional to lateral surface area).
        ' For a cylinder P(U,V) = C + R·cos(U)·e1 + R·sin(U)·e2 + V·axis,
        ' |dP/dU| = R, so the U-derivative magnitude gives the radius directly.
        Dim bestScore As Double = -1.0
        For i As Integer = 0 To axisVectors.Count - 1
            Dim dot As Double = dominantRef(0) * axisVectors(i)(0) +
                                dominantRef(1) * axisVectors(i)(1) +
                                dominantRef(2) * axisVectors(i)(2)
            If Math.Abs(dot) < COLLINEARITY_THRESHOLD Then Continue For

            Dim surf As EspritSolids.ISolidSurface = axisFaces(i).SolidSurface

            ' Only cylindrical faces are compatible with AlignAlongAxis.
            If surf.SurfaceType <> EspritSolids.SolidSurfaceType.geoSurfaceCylinder Then Continue For

            Dim uMin As Double = 0, uMax As Double = 0
            Dim vMin As Double = 0, vMax As Double = 0
            surf.SurfaceLimits(uMin, uMax, vMin, vMax)

            ' Start with UV-extent score; multiply by radius when available.
            Dim score As Double = (uMax - uMin) * (vMax - vMin)
            Try
                Dim res As Object = surf.Evaluate(
                    (uMin + uMax) / 2.0, (vMin + vMax) / 2.0, 1, 0)
                Dim dU As IComPoint = CType(res(1), IComPoint)
                Dim radius As Double = Math.Sqrt(dU.X * dU.X +
                                                  dU.Y * dU.Y +
                                                  dU.Z * dU.Z)
                If radius > 0.000001 Then score *= radius
            Catch
                ' Keep UV-extent-only score if radius evaluation fails.
            End Try

            If score > bestScore Then
                bestScore = score
                dominantFace = axisFaces(i)
            End If
        Next
    End Sub

    ''' <summary>
    ''' Returns the planar face with the largest UV parameter extent (proxy for
    ''' surface area). Returns Nothing if the solid has no planar face.
    ''' </summary>
    Private Function GetLargestPlanarFace(faces As EspritSolids.ISolidFaces) As EspritSolids.ISolidFace
        Dim best As EspritSolids.ISolidFace = Nothing
        Dim bestExtent As Double = -1.0

        For i As Long = 1 To faces.Count
            Dim face As EspritSolids.ISolidFace = faces.Item(i)
            Dim surf As EspritSolids.ISolidSurface = face.SolidSurface
            If surf.SurfaceType = EspritSolids.SolidSurfaceType.geoSurfacePlane Then
                Dim uMin As Double = 0, uMax As Double = 0
                Dim vMin As Double = 0, vMax As Double = 0
                surf.SurfaceLimits(uMin, uMax, vMin, vMax)
                Dim extent As Double = (uMax - uMin) * (vMax - vMin)
                If extent > bestExtent Then
                    bestExtent = extent
                    best = face
                End If
            End If
        Next
        Return best
    End Function

    ' ── Orientation check & flip ─────────────────────────────────────────────

    ''' <summary>
    ''' After AlignAlongAxis("Z"), re-evaluates the V-derivative of the
    ''' cylindrical face used for alignment. If it points toward -Z (inverted
    ''' axis), applies a 180-degree rotation around the X axis passing through
    ''' the solid's center of gravity.
    '''
    ''' Rotation strategy:
    '''   - Creates a temporary ESPRIT IPoint + ILine (Geometry Interfaces) that
    '''     define the X axis through the solid's center of gravity.
    '''   - Adds the solid to a temporary SelectionSet and calls Rotate(line, 180, 0).
    '''   - Removes the temporary geometry and SelectionSet when done.
    ''' </summary>
    Private Sub CheckAndFlipIfNeeded(doc As ESPRIT.Document,
                                      solid As ESPRIT.ISolid,
                                      face As EspritSolids.ISolidFace)
        Dim axis As Double() = ExtractAxisFromFace(face.SolidSurface)
        If axis Is Nothing Then Return

        ' axis(2) is the Z component of the revolution axis after alignment.
        ' If >= 0 the axis already points toward +Z — nothing to do.
        If axis(2) >= 0 Then Return

        ' Flip axis: X axis through the solid's center of gravity.
        ' Use ESPRIT Geometry Interfaces (IPoint, ILine) — not ComGeoBase types.
        Dim center As IComPoint = solid.GravityPoint
        Dim tempPoint As Object = doc.Points.Add(center.X, center.Y, center.Z)
        Dim tempLine As Object = doc.Lines.Add(tempPoint, 1.0, 0.0, 0.0)

        ' Remove any stale temporary selection set first.
        Try
            doc.SelectionSets.Remove(TEMP_SS_NAME)
        Catch
        End Try

        Dim ss As Object = doc.SelectionSets.Add(TEMP_SS_NAME)
        Try
            ss.Add(solid)
            ss.Rotate(tempLine, 180.0, 0)
        Finally
            Try
                doc.SelectionSets.Remove(TEMP_SS_NAME)
            Catch
            End Try
            Try
                doc.Lines.Remove(CInt(doc.Lines.IndexOf(tempLine)))
            Catch
            End Try
            Try
                doc.Points.Remove(CInt(doc.Points.IndexOf(tempPoint)))
            Catch
            End Try
        End Try
    End Sub

    ' ── P0 placement ─────────────────────────────────────────────────────────

    ''' <summary>
    ''' After alignment, computes the bounding box of the solid body and moves
    ''' P0 to (0, 0, maxZ) — only the Z coordinate is changed.
    '''
    ''' ISolidBody.Box() takes an IComMatrix that defines the reference frame for
    ''' the output corners. The solid's current CoordSystem is used: after
    ''' AlignAlongAxis("Z") the solid's Z axis coincides with world Z, so the Z
    ''' values returned by Box() are correct world-space Z extents.
    '''
    ''' A temporary IPoint is created at (0, 0, maxZ), passed to MoveP0, then
    ''' immediately removed from the document.
    ''' </summary>
    Private Sub MoveP0ToTopZ(doc As ESPRIT.Document, solid As ESPRIT.ISolid)
        Try
            Dim body As EspritSolids.ISolidBody = CType(solid.SolidBody, EspritSolids.ISolidBody)
            Dim minPt As EspritGeometryBase.IComPoint = Nothing
            Dim maxPt As EspritGeometryBase.IComPoint = Nothing

            ' solid.CoordSystem is accessed via late binding (Option Strict Off).
            ' After AlignAlongAxis("Z") this matrix has its Z axis aligned to world Z,
            ' so Box() returns world-space Z extents for the bounding box corners.
            Dim matrix As EspritGeometryBase.IComMatrix =
                CType(doc.Planes.Item("XYZ").GlobalToLocalMatrix, EspritGeometryBase.IComMatrix)

            body.Box(matrix, minPt, maxPt)

            ' Create a temporary point at (0, 0, maxZ) and move P0 to it.
            Dim tempPoint As Object = doc.Points.Add(0.0, 0.0, maxPt.Z)
            Try
                doc.MoveP0(tempPoint)
            Finally
                Try
                    doc.Points.Remove(CInt(doc.Points.IndexOf(tempPoint)))
                Catch
                End Try
            End Try
        Catch ex As Exception
            LogWarning($"Could not move P0 to top Z: {ex.Message}")
        End Try
    End Sub

    ' ── Logging helpers ──────────────────────────────────────────────────────

    Private Sub LogInfo(msg As String)
        _app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeInformation, LOG_SOURCE, msg)
    End Sub

    Private Sub LogWarning(msg As String)
        _app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeWarning, LOG_SOURCE, msg)
    End Sub

End Class
