Imports ESPRIT.NetApi.Ribbon
Imports EspritGeometryBase
Imports System.Windows.Forms
Imports System.Drawing

''' <summary>
''' Feature: Milling Alignment
''' Aligns the selected solid for milling in a single "Align Part" step:
'''   Phase 1 — Collect descriptors for every planar and cylindrical face
'''             (outward normal or axis direction, centroid, UV-area proxy).
'''   Phase 2 — Cluster faces by axis direction (|dot| > COLLINEARITY_THRESHOLD
'''             groups parallel and anti-parallel normals together).
'''   Phase 3 — Score each cluster: score = TotalArea / CentroidThickness.
'''             The axis where the most flat material lies AND the part is
'''             thinnest scores highest — the natural milling Z direction.
'''   Phase 4 — Split the winning cluster into top/bottom sub-groups;
'''             primary: more faces = machined top; tiebreaker: smaller largest
'''             single face = more fragmented = top.  Pick the largest planar face
'''             from the top sub-group for AlignAlongAxis.
'''   Phase 5 — AlignAlongAxis("Z") using that top face (or direct vector
'''             rotation as fallback when no planar face is available).
'''   Phase 6 — Orient XY: scan all horizontal planar faces (normal ≈ ±Z
'''             after alignment) for IComLine edges; rotate around Z so the
'''             longest line edge aligns with the X axis.
'''   Phase 7 — Move P0 to the top of the bounding box (maxZ).
''' </summary>
Public Class AlignMillingFeature
    Implements IFeature

    ' ── Ribbon keys ──────────────────────────────────────────────────────────
    Private Const RIBBON_GROUP_KEY As String = "AlignMilling_Group"
    Private Const BTN_ALIGN_KEY As String = "AlignMilling_Align_Btn"
    Private Const BTN_ALIGN_X_KEY As String = "AlignMilling_AlignX_Btn"
    Private Const BTN_ORIGIN_KEY As String = "AlignMilling_SetOrigin_Btn"

    Private Const LOG_SOURCE As String = "MillingAlignment"

    ''' <summary>
    ''' Minimum |dot product| for two unit normals to be grouped into the same
    ''' axis cluster.  Corresponds to a maximum angular deviation of ~10°.
    ''' cos(10°) ≈ 0.985
    ''' </summary>
    Private Const COLLINEARITY_THRESHOLD As Double = 0.985

    ''' <summary>
    ''' Minimum UV-extent area for a cylindrical face to be included as an axis
    ''' candidate.  Filters out small fillets, chamfers, and blend radii.
    ''' </summary>
    Private Const MIN_CYLINDER_AREA As Double = 10.0

    Private ReadOnly _app As ESPRIT.Application

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    ' ── Data types ────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Geometric descriptor for a single solid face (planar or cylindrical).
    ''' For planar faces  : Normal   = outward face normal (corrected by SameSenseAsSurface).
    ''' For cylinder faces: Normal   = axial direction (from dP/dV at parametric midpoint).
    ''' Centroid is the face centroid (PointAlong at UV centre, or axis midpoint for cylinders).
    ''' Area is the UV-extent proxy (uRange × vRange).
    ''' </summary>
    Private Class FaceInfo
        Public Normal As Double()                ' unit vector (3 elements)
        Public Centroid As Double()                ' world-space position (3 elements)
        Public Area As Double                  ' UV-extent proxy
        Public Face As EspritSolids.ISolidFace ' only set for planar faces
    End Class

    ''' <summary>
    ''' A group of faces sharing the same axis direction (within COLLINEARITY_THRESHOLD).
    ''' RefDirection is the unit axis set by the first face added to the cluster.
    ''' Faces with anti-parallel normals (the opposite side) are also included here.
    ''' </summary>
    Private Class AxisCluster
        Public RefDirection As Double()
        Public ReadOnly Faces As New List(Of FaceInfo)()

        Public ReadOnly Property TotalArea As Double
            Get
                Dim s As Double = 0
                For Each fi As FaceInfo In Faces : s += fi.Area : Next
                Return s
            End Get
        End Property
    End Class

    ' ── IFeature ─────────────────────────────────────────────────────────────

    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Dim icon As System.Drawing.Icon = LoadIcon()
        Dim group As IRibbonGroup = tab.Groups.Add(RIBBON_GROUP_KEY, "Milling Alignment")
        group.Items.AddButton(BTN_ALIGN_KEY, "Align Part", True, icon)
        group.Items.AddButton(BTN_ALIGN_X_KEY, "Align X", True, icon)
        group.Items.AddButton(BTN_ORIGIN_KEY, "Set Origin", True, icon)
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        Select Case e.Key
            Case BTN_ALIGN_KEY
                e.Handled = True
                AlignPart()
                Return True
            Case BTN_ALIGN_X_KEY
                e.Handled = True
                AlignX()
                Return True
            Case BTN_ORIGIN_KEY
                e.Handled = True
                SetBoundingBoxOrigin()
                Return True
        End Select
        Return False
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        ' No per-feature cleanup needed; the tab is removed by Main.
    End Sub

    ' ── Main action ───────────────────────────────────────────────────────────

    Private Sub AlignPart()
        Dim doc As ESPRIT.Document = _app.Document
        If doc Is Nothing Then LogWarning("No document is open.") : Return

        Dim body As EspritSolids.ISolidBody = GetSelectedSolidBody(doc)
        If body Is Nothing Then Return

        Dim faces As EspritSolids.ISolidFaces = body.SolidFaces

        ' ── Phase 1 : collect face descriptors ────────────────────────────────
        Dim allFaces As List(Of FaceInfo) = CollectFaceInfo(faces)
        If allFaces.Count = 0 Then
            LogWarning("No usable planar or cylindrical faces found.")
            Return
        End If

        ' ── Phase 2 : cluster by axis direction ───────────────────────────────
        Dim clusters As List(Of AxisCluster) = ClusterFaces(allFaces)

        ' ── Phase 3 : pick best Z cluster ─────────────────────────────────────
        Dim best As AxisCluster = PickBestCluster(clusters, allFaces)
        If best Is Nothing Then
            LogWarning("Could not determine a dominant alignment axis.")
            Return
        End If

        ' ── Phase 4 : identify the top face ───────────────────────────────────
        Dim topFace As EspritSolids.ISolidFace = PickTopFace(best)

        ' ── Phase 5 : align Z ─────────────────────────────────────────────────
        doc.Group.Clear()
        If topFace IsNot Nothing Then
            doc.Group.Add(topFace)
            doc.AlignAlongAxis("Z")
        Else
            ' Fallback: no planar face available — rotate directly.
            LogInfo("No planar top face found; using direct axis rotation.")
            AlignAxisToZ(doc, best.RefDirection)
        End If

        ' ── Phase 6 : orient XY (longest line edge → X axis) ──────────────────
        OrientXY(doc, body)

        ' ── Phase 7 : place P0 at top ──────────────────────────────────────────
        MoveP0ToTopZ(doc, body)

        LogInfo("Milling part aligned.")
        doc.Refresh()
    End Sub

    ' ── Phase 1 : face inventory ──────────────────────────────────────────────

    ''' <summary>
    ''' Returns geometric descriptors for all planar and relevant cylindrical faces.
    ''' </summary>
    Private Function CollectFaceInfo(faces As EspritSolids.ISolidFaces) As List(Of FaceInfo)
        Dim result As New List(Of FaceInfo)()

        For i As Long = 1 To faces.Count
            Dim face As EspritSolids.ISolidFace = faces.Item(i)
            Dim surf As EspritSolids.ISolidSurface = face.SolidSurface
            Try
                Select Case surf.SurfaceType
                    Case EspritSolids.SolidSurfaceType.geoSurfacePlane
                        Dim fi As FaceInfo = ExtractPlanarFaceInfo(face, surf)
                        If fi IsNot Nothing Then result.Add(fi)

                    Case EspritSolids.SolidSurfaceType.geoSurfaceCylinder
                        Dim fi As FaceInfo = ExtractCylinderFaceInfo(face, surf)
                        If fi IsNot Nothing Then result.Add(fi)
                End Select
            Catch
                ' Skip faces whose geometry cannot be read.
            End Try
        Next

        Return result
    End Function

    ''' <summary>
    ''' Extracts the outward normal, centroid, and UV-area of a planar face.
    ''' The outward normal is NormalAlong(UV centre) flipped when SameSenseAsSurface = False.
    ''' Returns Nothing when the normal is degenerate or geometry is unreadable.
    ''' </summary>
    Private Function ExtractPlanarFaceInfo(face As EspritSolids.ISolidFace,
                                           surf As EspritSolids.ISolidSurface) As FaceInfo
        Dim uMin As Double = 0, uMax As Double = 0, vMin As Double = 0, vMax As Double = 0
        face.FaceLimits(uMin, uMax, vMin, vMax)
        Dim uMid As Double = (uMin + uMax) / 2.0
        Dim vMid As Double = (vMin + vMax) / 2.0

        Dim n As IComVector = surf.NormalAlong(uMid, vMid)
        If n Is Nothing OrElse n.IsZero() Then Return Nothing
        n.Normalize()

        Dim flip As Double = If(face.SameSenseAsSurface, 1.0, -1.0)

        Dim p As IComPoint = surf.PointAlong(uMid, vMid)
        If p Is Nothing Then Return Nothing

        Return New FaceInfo With {
            .Normal = {n.X * flip, n.Y * flip, n.Z * flip},
            .Centroid = {p.X, p.Y, p.Z},
            .Area = (uMax - uMin) * (vMax - vMin),
            .Face = face
        }
    End Function

    ''' <summary>
    ''' Extracts the axis direction, axis midpoint, and UV-area of a cylindrical face.
    ''' Axis direction = dP/dV normalised at the parametric midpoint (exact for cylinders).
    ''' Filtered by MIN_CYLINDER_AREA to exclude small fillets and blend radii.
    ''' Returns Nothing for degenerate faces or faces below the area threshold.
    ''' </summary>
    Private Function ExtractCylinderFaceInfo(face As EspritSolids.ISolidFace,
                                              surf As EspritSolids.ISolidSurface) As FaceInfo
        Dim uMin As Double = 0, uMax As Double = 0, vMin As Double = 0, vMax As Double = 0
        face.FaceLimits(uMin, uMax, vMin, vMax)

        Dim area As Double = (uMax - uMin) * (vMax - vMin)
        If area < MIN_CYLINDER_AREA Then Return Nothing

        Dim uMid As Double = (uMin + uMax) / 2.0
        Dim vMid As Double = (vMin + vMax) / 2.0

        ' Axis direction from dP/dV (exactly along the revolution axis for cylinders).
        Dim evalV As Object = surf.Evaluate(uMid, vMid, 0, 1)
        Dim dvX As Double = CDbl(evalV(1).X)
        Dim dvY As Double = CDbl(evalV(1).Y)
        Dim dvZ As Double = CDbl(evalV(1).Z)
        Dim dvLen As Double = Math.Sqrt(dvX * dvX + dvY * dvY + dvZ * dvZ)
        If dvLen < 0.000001 Then Return Nothing

        ' Centroid: midpoint of the two axis-circle centres at vMin and vMax.
        Dim pBot As IComPoint = surf.PointAlong(uMid, vMin)
        Dim pTop As IComPoint = surf.PointAlong(uMid, vMax)
        If pBot Is Nothing OrElse pTop Is Nothing Then Return Nothing

        Return New FaceInfo With {
            .Normal = {dvX / dvLen, dvY / dvLen, dvZ / dvLen},
            .Centroid = {(pBot.X + pTop.X) / 2.0,
                         (pBot.Y + pTop.Y) / 2.0,
                         (pBot.Z + pTop.Z) / 2.0},
            .Area = area,
            .Face = Nothing   ' cylinders are used for Z scoring only, not for AlignAlongAxis
        }
    End Function

    ' ── Phase 2 : axis clustering ─────────────────────────────────────────────

    ''' <summary>
    ''' Groups face descriptors by axis direction.  Two faces are placed in the
    ''' same cluster when |dot(n1, n2)| > COLLINEARITY_THRESHOLD, meaning their
    ''' normals are parallel or anti-parallel (top/bottom of the same axis pair).
    ''' The first face added to a cluster sets RefDirection.
    ''' </summary>
    Private Function ClusterFaces(faces As List(Of FaceInfo)) As List(Of AxisCluster)
        Dim clusters As New List(Of AxisCluster)()

        For Each fi As FaceInfo In faces
            Dim placed As Boolean = False
            For Each c As AxisCluster In clusters
                Dim dot As Double = fi.Normal(0) * c.RefDirection(0) +
                                    fi.Normal(1) * c.RefDirection(1) +
                                    fi.Normal(2) * c.RefDirection(2)
                If Math.Abs(dot) > COLLINEARITY_THRESHOLD Then
                    c.Faces.Add(fi)
                    placed = True
                    Exit For
                End If
            Next
            If Not placed Then
                Dim nc As New AxisCluster()
                nc.RefDirection = fi.Normal
                nc.Faces.Add(fi)
                clusters.Add(nc)
            End If
        Next

        Return clusters
    End Function

    ' ── Phase 3 : best cluster selection ─────────────────────────────────────

    ''' <summary>
    ''' Scores each axis cluster and returns the one most likely to be the milling Z axis.
    '''
    ''' Score = TotalArea / CentroidThickness
    '''
    '''   TotalArea         — sum of UV-area proxies for all faces in the cluster.
    '''                       Rewards axes where large flat material is perpendicular.
    '''   CentroidThickness — range of ALL face centroids projected onto RefDirection.
    '''                       Rewards axes where the part is thin (flat plate geometry).
    '''
    ''' The combined score is highest for the axis that is simultaneously the "flat"
    ''' direction (many large faces perpendicular to it) and the "thin" direction of
    ''' the bounding extent.
    ''' </summary>
    Private Function PickBestCluster(clusters As List(Of AxisCluster),
                                     allFaceInfos As List(Of FaceInfo)) As AxisCluster
        Dim best As AxisCluster = Nothing
        Dim bestScore As Double = -1.0

        For Each c As AxisCluster In clusters
            If c.Faces.Count = 0 Then Continue For

            ' Thickness: range of ALL face centroids projected onto this axis.
            Dim projMin As Double = Double.MaxValue
            Dim projMax As Double = Double.MinValue
            For Each fi As FaceInfo In allFaceInfos
                Dim proj As Double = fi.Centroid(0) * c.RefDirection(0) +
                                     fi.Centroid(1) * c.RefDirection(1) +
                                     fi.Centroid(2) * c.RefDirection(2)
                If proj < projMin Then projMin = proj
                If proj > projMax Then projMax = proj
            Next

            Dim thickness As Double = Math.Max(projMax - projMin, 0.001)
            Dim score As Double = c.TotalArea / thickness

            If score > bestScore Then
                bestScore = score
                best = c
            End If
        Next

        Return best
    End Function

    ' ── Phase 4 : top face selection ──────────────────────────────────────────

    ''' <summary>
    ''' Within the winning cluster, splits planar faces into two sub-groups:
    '''   positive group — normals ≈ +RefDirection
    '''   negative group — normals ≈ -RefDirection
    ''' SelectTopGroup identifies the machined top sub-group (more faces = primary,
    ''' smaller largest face = tiebreaker). Returns the largest-area planar face from
    ''' the top sub-group for use with AlignAlongAxis.
    ''' Returns Nothing if the cluster contains no planar face.
    ''' </summary>
    Private Function PickTopFace(cluster As AxisCluster) As EspritSolids.ISolidFace
        Dim posGroup As New List(Of FaceInfo)()   ' normal ≈ +RefDirection
        Dim negGroup As New List(Of FaceInfo)()   ' normal ≈ -RefDirection

        For Each fi As FaceInfo In cluster.Faces
            If fi.Face Is Nothing Then Continue For   ' cylinder entry — skip
            If fi.Face.SolidSurface.SurfaceType <> EspritSolids.SolidSurfaceType.geoSurfacePlane Then
                Continue For
            End If

            Dim dot As Double = fi.Normal(0) * cluster.RefDirection(0) +
                                fi.Normal(1) * cluster.RefDirection(1) +
                                fi.Normal(2) * cluster.RefDirection(2)
            If dot >= 0 Then posGroup.Add(fi) Else negGroup.Add(fi)
        Next

        Dim topGroup As List(Of FaceInfo) = SelectTopGroup(posGroup, negGroup)
        If topGroup Is Nothing OrElse topGroup.Count = 0 Then Return Nothing

        Dim bestFi As FaceInfo = Nothing
        Dim bestArea As Double = -1.0
        For Each fi As FaceInfo In topGroup
            If fi.Area > bestArea Then bestArea = fi.Area : bestFi = fi
        Next

        Return If(bestFi IsNot Nothing, bestFi.Face, Nothing)
    End Function

    ''' <summary>
    ''' Returns the face group (posGroup or negGroup) that is the machined TOP side.
    '''
    ''' Primary heuristic — face count:
    '''   More planar faces in a group means more machined features (pockets, bosses,
    '''   islands) that fragment the surface.  The group with more faces is the top.
    '''
    ''' Tiebreaker — largest single face area:
    '''   When face counts are equal, the group whose largest individual face is
    '''   SMALLER is the more complex (fragmented) surface = machined top.
    '''   The group with the larger single face is the flat datum = bottom.
    '''
    ''' Both criteria are orientation-independent.
    ''' </summary>
    Private Function SelectTopGroup(posGroup As List(Of FaceInfo),
                                    negGroup As List(Of FaceInfo)) As List(Of FaceInfo)
        If posGroup.Count = 0 AndAlso negGroup.Count = 0 Then Return Nothing
        If posGroup.Count = 0 Then Return negGroup
        If negGroup.Count = 0 Then Return posGroup

        ' Primary: more faces = more machined features = top.
        If posGroup.Count <> negGroup.Count Then
            Return If(posGroup.Count > negGroup.Count, posGroup, negGroup)
        End If

        ' Tiebreaker: smaller largest single face = more fragmented = machined top.
        Dim posMaxArea As Double = 0
        For Each fi As FaceInfo In posGroup
            If fi.Area > posMaxArea Then posMaxArea = fi.Area
        Next
        Dim negMaxArea As Double = 0
        For Each fi As FaceInfo In negGroup
            If fi.Area > negMaxArea Then negMaxArea = fi.Area
        Next
        Return If(posMaxArea <= negMaxArea, posGroup, negGroup)
    End Function

    ' ── Phase 6 : orient XY ───────────────────────────────────────────────────

    ''' <summary>
    ''' After Z alignment, rotates the document around Z to minimise the XY
    ''' bounding-box area of the part (minimum oriented bounding box in 2-D).
    '''
    ''' Algorithm:
    '''   1. Collect 2-D points (X, Y) from every edge of every horizontal planar
    '''      face (normal ≈ ±Z) using IComGeoBounded.PointAlong().
    '''      Lines are sampled at 2 points (endpoints); curves (arcs, circles) at
    '''      ARC_SAMPLES + 1 evenly-spaced points along their arc length — this
    '''      correctly captures the "bulge" of circular edges.
    '''   2. Coarse sweep 0°…179° every 5° — find angle with minimum bbox area.
    '''   3. Fine sweep ±5° around the coarse best, every 0.25°.
    '''   4. Determine whether U or V is the longer dimension at the best angle;
    '''      if V is longer, add 90° so the longest axis aligns with X.
    '''   5. Apply rotation −bestAngle around Z.
    ''' </summary>
    Private Sub OrientXY(doc As ESPRIT.Document, body As EspritSolids.ISolidBody)
        ' ── Step 1 : collect 2-D point cloud via IComGeoBounded.PointAlong() ──
        Const ARC_SAMPLES As Integer = 8   ' intermediate samples for curved edges

        Dim pts As New List(Of Double())()   ' each element = {X, Y}

        Dim faces As EspritSolids.ISolidFaces = body.SolidFaces
        For i As Long = 1 To faces.Count
            Dim face As EspritSolids.ISolidFace = faces.Item(i)
            Dim surf As EspritSolids.ISolidSurface = face.SolidSurface
            If surf.SurfaceType <> EspritSolids.SolidSurfaceType.geoSurfacePlane Then Continue For

            ' Horizontal face filter (normal ≈ ±Z after Z alignment).
            Try
                Dim uMin As Double = 0, uMax As Double = 0, vMin As Double = 0, vMax As Double = 0
                face.FaceLimits(uMin, uMax, vMin, vMax)
                Dim n As IComVector = surf.NormalAlong((uMin + uMax) / 2.0, (vMin + vMax) / 2.0)
                If n Is Nothing OrElse n.IsZero() Then Continue For
                n.Normalize()
                If Math.Abs(n.Z) < COLLINEARITY_THRESHOLD Then Continue For
            Catch
                Continue For
            End Try

            ' Sample points along every edge using IComGeoBounded.PointAlong().
            ' Lines need only 2 points (endpoints); curved edges need intermediate
            ' samples to capture their geometric extent beyond the endpoints.
            Dim loops As EspritSolids.ISolidLoops = face.SolidLoops
            For j As Long = 1 To loops.Count
                Dim looop As EspritSolids.ISolidLoop = loops.Item(j)
                Dim edges As EspritSolids.ISolidEdges = looop.SolidEdges
                For k As Long = 1 To edges.Count
                    Try
                        Dim geom As Object = edges.Item(k).EdgeGeometry
                        Dim bounded As ComGeoBounded = TryCast(geom, ComGeoBounded)
                        If bounded Is Nothing OrElse bounded.Length <= 0 Then Continue For
                        Dim totalLen As Double = bounded.Length
                        Dim steps As Integer = If(TypeOf geom Is IComLine, 1, ARC_SAMPLES)
                        For s As Integer = 0 To steps
                            Dim pt As IComPoint = bounded.PointAlong(totalLen * s / steps)
                            If pt IsNot Nothing Then pts.Add(New Double() {pt.X, pt.Y})
                        Next
                    Catch
                    End Try
                Next
            Next
        Next

        If pts.Count < 2 Then
            LogInfo("Orient XY: insufficient vertex data — XY orientation unchanged.")
            Return
        End If

        ' ── Step 2 : coarse sweep 0°…179° every 5° ────────────────────────────
        Dim coarseStep As Double = 5.0 * Math.PI / 180.0
        Dim fineStep As Double = 0.25 * Math.PI / 180.0

        Dim bestAngle As Double = 0.0
        Dim bestArea As Double = Double.MaxValue

        Dim theta As Double = 0.0
        Do While theta < Math.PI
            Dim area As Double = BBoxArea2D(pts, theta)
            If area < bestArea Then bestArea = area : bestAngle = theta
            theta += coarseStep
        Loop

        ' ── Step 3 : fine sweep ±5° around the coarse best ────────────────────
        ' Bounds are fixed BEFORE the loop — bestAngle must not shift the end condition.
        Dim fineLo As Double = bestAngle - coarseStep
        Dim fineHi As Double = bestAngle + coarseStep
        theta = fineLo
        Do While theta <= fineHi
            Dim area As Double = BBoxArea2D(pts, theta)
            If area < bestArea Then bestArea = area : bestAngle = theta
            theta += fineStep
        Loop

        ' ── Step 4 : align the LONGER dimension to X ──────────────────────────
        Dim cosB As Double = Math.Cos(bestAngle)
        Dim sinB As Double = Math.Sin(bestAngle)
        Dim bbUMin As Double = Double.MaxValue, bbUMax As Double = Double.MinValue
        Dim bbVMin As Double = Double.MaxValue, bbVMax As Double = Double.MinValue
        For Each pt As Double() In pts
            Dim u As Double = pt(0) * cosB + pt(1) * sinB
            Dim v As Double = -pt(0) * sinB + pt(1) * cosB
            If u < bbUMin Then bbUMin = u
            If u > bbUMax Then bbUMax = u
            If v < bbVMin Then bbVMin = v
            If v > bbVMax Then bbVMax = v
        Next
        If (bbVMax - bbVMin) > (bbUMax - bbUMin) Then bestAngle += Math.PI / 2.0

        ' Normalise to [-90°, +90°] for the smallest possible rotation.
        If bestAngle > Math.PI / 2.0 Then bestAngle -= Math.PI
        If bestAngle < -Math.PI / 2.0 Then bestAngle += Math.PI

        ' ── Step 5 : apply rotation ────────────────────────────────────────────
        Const ANGLE_TOL As Double = 0.25 * Math.PI / 180.0   ' skip rotations < 0.25°
        If Math.Abs(bestAngle) < ANGLE_TOL Then
            LogInfo("Orient XY: part already optimally oriented — no XY rotation needed.")
            Return
        End If

        RotateAllDocumentObjects(doc, 0.0, 0.0, 1.0, -bestAngle)
        LogInfo($"Orient XY: rotated {-bestAngle * 180.0 / Math.PI:F2}° around Z (min bbox).")
    End Sub

    ''' <summary>
    ''' Projects the 2-D point cloud onto the axis at <paramref name="theta"/> and
    ''' its perpendicular, then returns the bounding-rectangle area.
    ''' Used by <see cref="OrientXY"/> to score candidate rotation angles.
    ''' </summary>
    Private Function BBoxArea2D(pts As List(Of Double()), theta As Double) As Double
        Dim cosT As Double = Math.Cos(theta)
        Dim sinT As Double = Math.Sin(theta)
        Dim uMin As Double = Double.MaxValue, uMax As Double = Double.MinValue
        Dim vMin As Double = Double.MaxValue, vMax As Double = Double.MinValue
        For Each pt As Double() In pts
            Dim u As Double = pt(0) * cosT + pt(1) * sinT
            Dim v As Double = -pt(0) * sinT + pt(1) * cosT
            If u < uMin Then uMin = u
            If u > uMax Then uMax = u
            If v < vMin Then vMin = v
            If v > vMax Then vMax = v
        Next
        Return (uMax - uMin) * (vMax - vMin)
    End Function

    ' ── Align fallback ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Directly rotates all document objects to align the given unit axis to +Z.
    ''' Used when no planar face is available to pass to doc.AlignAlongAxis.
    ''' </summary>
    Private Sub AlignAxisToZ(doc As ESPRIT.Document, axis As Double())
        Dim dotZ As Double = axis(2)   ' dot with (0, 0, 1)
        If Math.Abs(dotZ) > 0.9999 Then
            If dotZ < 0 Then RotateAllDocumentObjects(doc, 1.0, 0.0, 0.0, Math.PI)
            Return
        End If

        Dim rx As Double = axis(1)
        Dim ry As Double = -axis(0)
        Dim rLen As Double = Math.Sqrt(rx * rx + ry * ry)
        If rLen < 0.000001 Then Return

        Dim angle As Double = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dotZ)))
        RotateAllDocumentObjects(doc, rx / rLen, ry / rLen, 0.0, angle)
    End Sub

    ' ── P0 placement ─────────────────────────────────────────────────────────

    ''' <summary>
    ''' Computes the axis-aligned bounding box of the solid body and moves P0
    ''' to the XY centre of the bounding box at maxZ, so the program origin sits
    ''' centred over the part at its top face.
    ''' </summary>
    Private Sub MoveP0ToTopZ(doc As ESPRIT.Document, body As EspritSolids.ISolidBody)
        Try
            Dim minPt As IComPoint = Nothing
            Dim maxPt As IComPoint = Nothing
            Dim matrix As IComMatrix = CType(doc.Planes.Item("XYZ").GlobalToLocalMatrix, IComMatrix)

            body.Box(matrix, minPt, maxPt)

            Dim centerX As Double = (minPt.X + maxPt.X) / 2.0
            Dim centerY As Double = (minPt.Y + maxPt.Y) / 2.0
            Dim tempPoint As Object = doc.Points.Add(centerX, centerY, maxPt.Z)
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

    ' ── Bounding box origin placement ─────────────────────────────────────────

    ''' <summary>
    ''' Opens a dialog for the user to choose a point on the bounding box,
    ''' then moves P0 to the computed world position.
    ''' The solid body is resolved from the current selection before the dialog
    ''' appears, so the user sees the dialog only when a valid selection exists.
    ''' </summary>
    Private Sub SetBoundingBoxOrigin()
        Dim doc As ESPRIT.Document = _app.Document
        If doc Is Nothing Then LogWarning("No document is open.") : Return

        Dim body As EspritSolids.ISolidBody = GetSelectedSolidBody(doc)
        If body Is Nothing Then Return

        Using dlg As New BoundingBoxOriginDialog()
            If dlg.ShowDialog() <> DialogResult.OK Then Return

            Try
                Dim minPt As IComPoint = Nothing
                Dim maxPt As IComPoint = Nothing
                Dim matrix As IComMatrix = CType(doc.Planes.Item("XYZ").GlobalToLocalMatrix, IComMatrix)
                body.Box(matrix, minPt, maxPt)

                Dim targetX As Double = minPt.X + (maxPt.X - minPt.X) * dlg.XFraction
                Dim targetY As Double = minPt.Y + (maxPt.Y - minPt.Y) * dlg.YFraction
                Dim targetZ As Double = minPt.Z + (maxPt.Z - minPt.Z) * dlg.ZFraction

                Dim tempPoint As Object = doc.Points.Add(targetX, targetY, targetZ)
                Try
                    doc.MoveP0(tempPoint)
                Finally
                    Try
                        doc.Points.Remove(CInt(doc.Points.IndexOf(tempPoint)))
                    Catch
                    End Try
                End Try

                LogInfo($"Origin set to ({targetX:F3}, {targetY:F3}, {targetZ:F3}).")
                doc.Refresh()
            Catch ex As Exception
                LogWarning($"Could not set origin: {ex.Message}")
            End Try
        End Using
    End Sub

    ' ── Bounding box origin dialog ─────────────────────────────────────────────

    ''' <summary>
    ''' Dialog for selecting an origin point on the axis-aligned bounding box.
    '''
    ''' XY panel  — 3×3 grid of radio buttons schematising the rectangle face:
    '''             corners at the 4 corners, midpoints at the 4 edge centres,
    '''             and one button at the geometric centre.
    '''             Lines are drawn between the buttons to make the rectangle visible.
    '''
    ''' Z panel   — 3 stacked radio buttons: Z+ (maxZ), Z Center (midZ), Z− (minZ).
    '''
    ''' Fractions in [0, 1]: 0 = min, 0.5 = centre, 1 = max along the bounding box.
    ''' Defaults: XY centre, Z+ (top of the solid).
    ''' </summary>
    Private Class BoundingBoxOriginDialog
        Inherits Form

        Public XFraction As Double = 0.5   ' 0=minX … 1=maxX
        Public YFraction As Double = 1.0   ' 0=minY … 1=maxY  (default: top of rect)
        Public ZFraction As Double = 1.0   ' 0=minZ … 1=maxZ  (default: Z+)

        ' _rbXY(col, row): col 0=left/minX … 2=right/maxX
        '                  row 0=top/maxY (visual) … 2=bottom/minY (visual)
        Private _rbXY(2, 2) As RadioButton
        Private _rbZ(2) As RadioButton      ' 0=Z+, 1=ZCenter, 2=Z−

        ' Dot-centre pixel positions used both for layout and for Paint.
        Private _dotCX() As Integer
        Private _dotCY() As Integer

        ' Last selection — persisted in the registry across sessions.
        Private Const PREF_REG_PATH As String = "Software\HXG_Extension_France\BoundingBoxOrigin"
        Private Shared _lastXYCol As Integer = 1   ' default: centre column
        Private Shared _lastXYRow As Integer = 1   ' default: centre row
        Private Shared _lastZIdx  As Integer = 0   ' default: Z+
        Private Shared _prefsLoaded As Boolean = False

        Public Sub New()
            If Not _prefsLoaded Then
                _prefsLoaded = True
                Try
                    Using key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PREF_REG_PATH)
                        If key IsNot Nothing Then
                            _lastXYCol = Math.Max(0, Math.Min(2, CInt(key.GetValue("XYCol", 1))))
                            _lastXYRow = Math.Max(0, Math.Min(2, CInt(key.GetValue("XYRow", 1))))
                            _lastZIdx  = Math.Max(0, Math.Min(2, CInt(key.GetValue("ZIdx",  0))))
                        End If
                    End Using
                Catch
                End Try
            End If

            Me.Text = "Set Bounding Box Origin"
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.StartPosition = FormStartPosition.CenterScreen
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ClientSize = New Size(400, 280)
            BuildXYGroup()
            BuildZGroup()
            BuildButtons()
        End Sub

        ' ── XY group ──────────────────────────────────────────────────────────

        Private Sub BuildXYGroup()
            Dim gb As New GroupBox()
            gb.Text = "XY Position"
            gb.Location = New Point(10, 10)
            gb.Size = New Size(245, 215)
            Me.Controls.Add(gb)

            Dim pnl As New Panel()
            pnl.Location = New Point(10, 22)
            pnl.Size = New Size(225, 185)
            gb.Controls.Add(pnl)

            ' Radio button top-left positions (within the panel).
            Dim colX() As Integer = {16, 96, 176}
            Dim rowY() As Integer = {16, 76, 136}

            ' The visual dot of a RadioButton (no text) is centred ~7 px from top-left.
            _dotCX = New Integer() {colX(0) + 7, colX(1) + 7, colX(2) + 7}
            _dotCY = New Integer() {rowY(0) + 7, rowY(1) + 7, rowY(2) + 7}

            For col As Integer = 0 To 2
                For row As Integer = 0 To 2
                    Dim rb As New RadioButton()
                    rb.Location = New Point(colX(col), rowY(row))
                    rb.Size = New Size(20, 20)
                    rb.AutoSize = False
                    rb.Text = ""
                    pnl.Controls.Add(rb)
                    _rbXY(col, row) = rb
                Next
            Next

            ' Restore last selection (or default: centre).
            _rbXY(_lastXYCol, _lastXYRow).Checked = True

            AddHandler pnl.Paint, AddressOf DrawXYGrid
        End Sub

        ''' <summary>
        ''' Draws the rectangle schematic behind the radio buttons:
        ''' solid lines for the outer border, dashed lines for the centre cross.
        ''' </summary>
        Private Sub DrawXYGrid(sender As Object, e As PaintEventArgs)
            Dim g As Graphics = e.Graphics
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

            Using pen As New Pen(Color.DimGray, 1.5F)
                ' Outer rectangle
                g.DrawLine(pen, _dotCX(0), _dotCY(0), _dotCX(2), _dotCY(0))  ' top
                g.DrawLine(pen, _dotCX(0), _dotCY(2), _dotCX(2), _dotCY(2))  ' bottom
                g.DrawLine(pen, _dotCX(0), _dotCY(0), _dotCX(0), _dotCY(2))  ' left
                g.DrawLine(pen, _dotCX(2), _dotCY(0), _dotCX(2), _dotCY(2))  ' right
            End Using

            Using dashPen As New Pen(Color.Silver, 1.0F)
                dashPen.DashStyle = Drawing2D.DashStyle.Dash
                ' Centre cross
                g.DrawLine(dashPen, _dotCX(0), _dotCY(1), _dotCX(2), _dotCY(1))  ' horizontal
                g.DrawLine(dashPen, _dotCX(1), _dotCY(0), _dotCX(1), _dotCY(2))  ' vertical
            End Using
        End Sub

        ' ── Z group ───────────────────────────────────────────────────────────

        Private Sub BuildZGroup()
            Dim gb As New GroupBox()
            gb.Text = "Z Position"
            gb.Location = New Point(268, 10)
            gb.Size = New Size(118, 120)
            Me.Controls.Add(gb)

            Dim labels() As String = {"Z+", "Z Center", "Z−"}
            For i As Integer = 0 To 2
                Dim rb As New RadioButton()
                rb.Text = labels(i)
                rb.Location = New Point(12, 25 + i * 32)
                rb.AutoSize = True
                gb.Controls.Add(rb)
                _rbZ(i) = rb
            Next
            _rbZ(_lastZIdx).Checked = True  ' Restore last selection (or default: Z+)
        End Sub

        ' ── Buttons ───────────────────────────────────────────────────────────

        Private Sub BuildButtons()
            Dim btnOK As New Button()
            btnOK.Text = "OK"
            btnOK.Location = New Point(228, 244)
            btnOK.Size = New Size(75, 26)
            Me.Controls.Add(btnOK)
            Me.AcceptButton = btnOK
            AddHandler btnOK.Click, AddressOf OnOKClick

            Dim btnCancel As New Button()
            btnCancel.Text = "Cancel"
            btnCancel.DialogResult = DialogResult.Cancel
            btnCancel.Location = New Point(313, 244)
            btnCancel.Size = New Size(75, 26)
            Me.Controls.Add(btnCancel)
            Me.CancelButton = btnCancel
        End Sub

        Private Sub OnOKClick(sender As Object, e As EventArgs)
            ' Read XY selection.
            Dim selCol As Integer = 1, selRow As Integer = 1
            Dim found As Boolean = False
            For col As Integer = 0 To 2
                For row As Integer = 0 To 2
                    If _rbXY(col, row).Checked Then
                        selCol = col : selRow = row
                        XFraction = col * 0.5          ' 0=minX, 0.5=midX, 1=maxX
                        YFraction = (2 - row) * 0.5    ' visual row 0 → maxY, row 2 → minY
                        found = True
                        Exit For
                    End If
                Next
                If found Then Exit For
            Next

            ' Read Z selection.
            Dim selZIdx As Integer = If(_rbZ(0).Checked, 0, If(_rbZ(1).Checked, 1, 2))
            ZFraction = If(selZIdx = 0, 1.0, If(selZIdx = 1, 0.5, 0.0))

            ' Persist selection for next session.
            _lastXYCol = selCol : _lastXYRow = selRow : _lastZIdx = selZIdx
            Try
                Using key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PREF_REG_PATH)
                    key.SetValue("XYCol", selCol)
                    key.SetValue("XYRow", selRow)
                    key.SetValue("ZIdx",  selZIdx)
                End Using
            Catch
            End Try

            Me.DialogResult = DialogResult.OK
            Me.Close()
        End Sub

    End Class

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
