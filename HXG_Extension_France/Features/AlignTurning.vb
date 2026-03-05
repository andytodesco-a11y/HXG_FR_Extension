Imports ESPRIT.NetApi.Ribbon
Imports EspritGeometryBase

''' <summary>
''' Feature: Turning Alignment
''' Provides step-by-step turning part alignment tools:
'''   - Align Part : aligns the selected solid along Z using the dominant revolution axis.
'''   - Flip Part  : rotates the solid 180° around the Y axis through the world origin.
'''   - Orient C   : (placeholder)
''' </summary>
Public Class AlignTurningFeature
    Implements IFeature

    ' ── Ribbon keys ──────────────────────────────────────────────────────────
    Private Const RIBBON_GROUP_KEY As String = "AlignTurning_Group"
    Private Const BTN_ALIGN_KEY As String = "AlignTurning_Align_Btn"
    Private Const BTN_FLIP_KEY As String = "AlignTurning_Flip_Btn"
    Private Const BTN_ORIENT_C_KEY As String = "AlignTurning_OrientC_Btn"

    Private Const LOG_SOURCE As String = "TurningAlignment"
    Private Const TEMP_SS_NAME As String = "_HXG_TempRotate"

    ''' <summary>
    ''' Minimum |dot product| for two unit axis vectors to be considered collinear
    ''' during clustering and edge-normal filtering.
    ''' Corresponds to a maximum angular deviation of ~10 degrees.
    ''' </summary>
    Private Const COLLINEARITY_THRESHOLD As Double = 0.985

    ''' <summary>
    ''' Stricter |dot product| threshold used when selecting the face passed to
    ''' doc.AlignAlongAxis. A face whose axis deviates more than ~1° from the
    ''' dominant cluster direction is rejected, even if it joined the cluster under
    ''' the looser COLLINEARITY_THRESHOLD.  This prevents a slightly-tilted internal
    ''' bore from being used as the alignment reference.
    ''' cos(1.1°) ≈ 0.9998
    ''' </summary>
    Private Const ALIGNMENT_THRESHOLD As Double = 0.9998

    ''' <summary>
    ''' Maximum perpendicular distance (mm) between two axis lines for them to be
    ''' considered coaxial. Parallel axes whose lines are further apart are placed
    ''' in separate clusters (e.g. a radial bore offset from the main spindle axis).
    ''' </summary>
    Private Const COAXIAL_TOLERANCE As Double = 1.0

    Private ReadOnly _app As ESPRIT.Application

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    ' ── Data types ────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Descriptor for a single cylindrical or conical solid face:
    ''' its radius and its axial extent projected on the cluster reference direction.
    ''' </summary>
    Private Class CylinderData
        Public Radius As Double
        Public TMin As Double   ' lower axial projection on the cluster's RefDirection
        Public TMax As Double   ' upper axial projection on the cluster's RefDirection
        Public AngularSpan As Double   ' face angular span in radians, capped at 2π
    End Class

    ''' <summary>Closed 1-D interval [TMin, TMax] used for interval-union arithmetic.</summary>
    Private Class Interval
        Public TMin As Double
        Public TMax As Double
        Public Sub New(tMin As Double, tMax As Double)
            Me.TMin = tMin
            Me.TMax = tMax
        End Sub
    End Class

    ''' <summary>
    ''' A group of cylindrical/conical solid faces that share the same geometric axis line
    ''' (collinear direction AND coaxial position within COAXIAL_TOLERANCE).
    ''' </summary>
    Private Class AxisLineCluster
        Public RefDirection As Double()                        ' unit vector along the revolution axis
        Public RefPoint As Double()                        ' one point on the axis line
        Public ReadOnly Cylinders As New List(Of CylinderData)()
    End Class

    ' ── IFeature ─────────────────────────────────────────────────────────────

    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Dim icon As System.Drawing.Icon = LoadIcon()
        Dim group As IRibbonGroup = tab.Groups.Add(RIBBON_GROUP_KEY, "Turning Alignment")
        group.Items.AddButton(BTN_ALIGN_KEY, "Align Part", True, icon)
        group.Items.AddButton(BTN_FLIP_KEY, "Flip Part", True, icon)
        group.Items.AddButton(BTN_ORIENT_C_KEY, "Orient C", True, icon)
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        Select Case e.Key
            Case BTN_ALIGN_KEY
                e.Handled = True
                AlignPart()
                Return True
            Case BTN_FLIP_KEY
                e.Handled = True
                FlipPart()
                Return True
            Case BTN_ORIENT_C_KEY
                e.Handled = True
                DebugAnalyzeSolid()
                Return True
        End Select
        Return False
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        ' No per-feature cleanup needed; the tab is removed by Main.
    End Sub

    ' ── Actions ──────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Debug: analyses every cylindrical/conical face of the selected solid and logs
    ''' to the EventWindow:
    '''   • For each face: radius, axial length, angular span (from edges), computed volume.
    '''   • For each arc edge: raw StartAngle / EndAngle (to detect degrees vs radians).
    '''   • Cluster summary: direction, face count, total volume.
    '''   • Which cluster wins.
    ''' Triggered by the "Orient C" button.  Remove or disable after debugging.
    ''' </summary>
    Private Sub DebugAnalyzeSolid()
        Dim doc As ESPRIT.Document = _app.Document
        If doc Is Nothing Then LogWarning("No document is open.") : Return

        Dim solid As ESPRIT.ISolid = GetSelectedSolid(doc)
        If solid Is Nothing Then Return

        Dim body As EspritSolids.ISolidBody = CType(solid.SolidBody, EspritSolids.ISolidBody)
        Dim faces As EspritSolids.ISolidFaces = body.SolidFaces
        LogInfo("=== DEBUG AlignTurning — face analysis ===")
        LogInfo($"Total faces in solid: {faces.Count}")

        ' ── Phase 1 : per-face geometry ──────────────────────────────────────
        Dim faceIndex As Integer = 0
        For i As Long = 1 To faces.Count
            Dim face As EspritSolids.ISolidFace = faces.Item(i)
            Dim stype As EspritSolids.SolidSurfaceType = face.SolidSurface.SurfaceType
            If stype <> EspritSolids.SolidSurfaceType.geoSurfaceCylinder AndAlso
               stype <> EspritSolids.SolidSurfaceType.geoSurfaceCone Then Continue For

            faceIndex += 1
            Dim typeName As String = If(stype = EspritSolids.SolidSurfaceType.geoSurfaceCylinder, "CYL", "CONE")

            Try
                ' ── Raw FaceLimits (parametric domain) ──
                Dim uMinR As Double = 0, uMaxR As Double = 0, vMinR As Double = 0, vMaxR As Double = 0
                face.FaceLimits(uMinR, uMaxR, vMinR, vMaxR)
                Dim paramSpanDeg As Double = Math.Min(Math.Abs(uMaxR - uMinR), 2.0 * Math.PI) * 180.0 / Math.PI
                LogInfo($"  [{faceIndex}] {typeName}  uMin={uMinR:F4} uMax={uMaxR:F4}  paramSpan={paramSpanDeg:F1}°")

                ' ── Extract geometry ──
                Dim direction(2) As Double
                Dim centerBot(2) As Double
                Dim radius As Double = 0
                Dim tMin As Double = 0
                Dim tMax As Double = 0
                Dim angularSpan As Double = 0

                If Not ExtractCylinderGeometry(face, direction, centerBot, radius, tMin, tMax, angularSpan) Then
                    LogInfo($"          ExtractCylinderGeometry FAILED")
                    Continue For
                End If

                Dim axialLen As Double = tMax - tMin
                Dim volume As Double = (angularSpan / 2.0) * radius * radius * axialLen
                Dim spanDeg As Double = angularSpan * 180.0 / Math.PI

                LogInfo($"          R={radius:F2}mm  L={axialLen:F2}mm  span={spanDeg:F1}°  vol={volume:F0}mm³")
                LogInfo($"          axis=({direction(0):F3},{direction(1):F3},{direction(2):F3})")
                LogInfo($"          center=({centerBot(0):F3},{centerBot(1):F3},{centerBot(2):F3})")

                ' ── Scan edges: arc / circle details ──
                Dim loops As EspritSolids.ISolidLoops = face.SolidLoops
                For j As Long = 1 To loops.Count
                    Dim looop As EspritSolids.ISolidLoop = loops.Item(j)
                    Dim edges As EspritSolids.ISolidEdges = looop.SolidEdges
                    For k As Long = 1 To edges.Count
                        Dim geo As Object = edges.Item(k).EdgeGeometry
                        If geo Is Nothing Then Continue For
                        Try
                            If TypeOf geo Is IComArc Then
                                Dim arc As IComArc = CType(geo, IComArc)
                                Dim n As IComVector = arc.Normal()
                                Dim nDot As Double = 0
                                Dim onAxis As Boolean = False
                                If n IsNot Nothing AndAlso Not n.IsZero() Then
                                    n.Normalize()
                                    nDot = Math.Abs(n.X * direction(0) + n.Y * direction(1) + n.Z * direction(2))
                                    If nDot >= COLLINEARITY_THRESHOLD Then
                                        onAxis = IsOnAxisLine(arc.CenterPoint, direction, centerBot)
                                    End If
                                End If
                                Dim raw As Double = arc.EndAngle - arc.StartAngle
                                Dim sweep As Double = If(raw > 0, raw, raw + 2.0 * Math.PI)
                                LogInfo($"          loop[{j}] edge[{k}] ARC  sweep={sweep * 180.0 / Math.PI:F1}°  n·axis={nDot:F3}  onAxis={onAxis}")
                            ElseIf TypeOf geo Is IComCircle Then
                                Dim circle As IComCircle = CType(geo, IComCircle)
                                Dim n As IComVector = circle.Normal()
                                Dim nDot As Double = 0
                                Dim onAxis As Boolean = False
                                If n IsNot Nothing AndAlso Not n.IsZero() Then
                                    n.Normalize()
                                    nDot = Math.Abs(n.X * direction(0) + n.Y * direction(1) + n.Z * direction(2))
                                    If nDot >= COLLINEARITY_THRESHOLD Then
                                        onAxis = IsOnAxisLine(circle.CenterPoint, direction, centerBot)
                                    End If
                                End If
                                LogInfo($"          loop[{j}] edge[{k}] CIRCLE(full)  n·axis={nDot:F3}  onAxis={onAxis}")
                            Else
                                LogInfo($"          loop[{j}] edge[{k}] OTHER ({geo.GetType().Name})")
                            End If
                        Catch ex2 As Exception
                            LogInfo($"          loop[{j}] edge[{k}] ERROR: {ex2.Message}")
                        End Try
                    Next
                Next

            Catch ex As Exception
                LogInfo($"  [{faceIndex}] {typeName} — ERROR: {ex.Message}")
            End Try
        Next

        ' ── Phase 2 : cluster summary (mirrors GetDominantAxisLine) ──────────
        LogInfo("--- Cluster analysis ---")
        Dim clusters As New List(Of AxisLineCluster)()
        For i As Long = 1 To faces.Count
            Dim face As EspritSolids.ISolidFace = faces.Item(i)
            Dim stype As EspritSolids.SolidSurfaceType = face.SolidSurface.SurfaceType
            If stype <> EspritSolids.SolidSurfaceType.geoSurfaceCylinder AndAlso
               stype <> EspritSolids.SolidSurfaceType.geoSurfaceCone Then Continue For
            Try
                Dim direction(2) As Double
                Dim centerBot(2) As Double
                Dim radius As Double = 0
                Dim tMin As Double = 0
                Dim tMax As Double = 0
                Dim angularSpan As Double = 0
                If ExtractCylinderGeometry(face, direction, centerBot, radius, tMin, tMax, angularSpan) Then
                    AccumulateIntoAxisLineCluster(clusters, direction, centerBot, radius, tMin, tMax, angularSpan)
                End If
            Catch
            End Try
        Next

        Dim winnerIdx As Integer = -1
        Dim maxVol As Double = -1.0
        For ci As Integer = 0 To clusters.Count - 1
            Dim c As AxisLineCluster = clusters(ci)
            ' Build a temporary copy so MergeCoCylindricalFaces (called inside
            ' ComputeClusterVolume) does not alter the list we are iterating.
            Dim tempCluster As New AxisLineCluster()
            tempCluster.RefDirection = c.RefDirection
            tempCluster.RefPoint = c.RefPoint
            For Each cyl As CylinderData In c.Cylinders
                tempCluster.Cylinders.Add(New CylinderData With {
                    .Radius = cyl.Radius,
                    .TMin = cyl.TMin,
                    .TMax = cyl.TMax,
                    .AngularSpan = cyl.AngularSpan
                })
            Next
            Dim vol As Double = ComputeClusterVolume(tempCluster)
            Dim dir As Double() = c.RefDirection
            LogInfo($"  Cluster[{ci}]  axis=({dir(0):F3},{dir(1):F3},{dir(2):F3})" &
                    $"  faces={c.Cylinders.Count}  vol={vol:F0}mm³")
            ' Show each entry after merging (tempCluster.Cylinders was sorted+merged by ComputeClusterVolume)
            For Each cyl As CylinderData In tempCluster.Cylinders
                Dim spanDeg As Double = cyl.AngularSpan * 180.0 / Math.PI
                LogInfo($"    R={cyl.Radius:F2}  [{cyl.TMin:F1},{cyl.TMax:F1}]  span={spanDeg:F1}°")
            Next
            If vol > maxVol Then maxVol = vol : winnerIdx = ci
        Next

        If winnerIdx >= 0 Then
            Dim w As Double() = clusters(winnerIdx).RefDirection
            LogInfo($"  => Dominant axis: Cluster[{winnerIdx}]  ({w(0):F3},{w(1):F3},{w(2):F3})  vol={maxVol:F0}mm³")
        End If

        LogInfo("=== END DEBUG ===")
    End Sub

    ''' <summary>
    ''' Two-phase alignment:
    '''   Phase 1 — GetDominantAxisLine : finds the dominant revolution axis by
    '''             accumulating volume (π×r²×length) for each coaxial cluster.
    '''             Cylinders are processed radius-descending so that OD surfaces
    '''             claim their axial interval before inner bores are evaluated.
    '''   Phase 2 — FindBestCylinderFace : within that axis, selects the
    '''             geoSurfaceCylinder face with the highest Radius × Height score
    '''             so that ESPRIT's AlignAlongAxis gets an accurate reference.
    ''' Falls back to a direct vector rotation if no cylindrical face is found.
    ''' </summary>
    Private Sub AlignPart()
        Dim doc As ESPRIT.Document = _app.Document
        If doc Is Nothing Then LogWarning("No document is open.") : Return

        Dim solid As ESPRIT.ISolid = GetSelectedSolid(doc)
        If solid Is Nothing Then Return

        Dim body As EspritSolids.ISolidBody = CType(solid.SolidBody, EspritSolids.ISolidBody)

        Dim dominant As AxisLineCluster = GetDominantAxisLine(body)
        If dominant Is Nothing Then
            LogWarning("No revolution axis found. Ensure a turning part is selected.")
            Return
        End If

        Dim cylFace As EspritSolids.ISolidFace = FindBestCylinderFace(body, dominant.RefDirection)
        If cylFace IsNot Nothing Then
            ' Preferred path: let ESPRIT align via the best cylindrical face.
            solid.Grouped = False
            doc.Group.Add(cylFace)
            doc.AlignAlongAxis("Z")
        Else
            ' Fallback: no geoSurfaceCylinder in dominant cluster — rotate directly.
            AlignAxisToZ(doc, dominant.RefDirection)
        End If

        MoveP0ToTopZ(doc, solid)
        LogInfo("Turning part aligned along Z axis.")
        doc.Refresh()
    End Sub

    ''' <summary>
    ''' Rotates the selected solid 180° around the Y axis through the world origin.
    ''' </summary>
    Private Sub FlipPart()
        Dim doc As ESPRIT.Document = _app.Document
        If doc Is Nothing Then LogWarning("No document is open.") : Return

        Dim solid As ESPRIT.ISolid = GetSelectedSolid(doc)
        If solid Is Nothing Then Return

        FlipSolid180(doc, solid)
        LogInfo("Part flipped 180° around Y axis through center of gravity.")
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

    ' ── Phase 1 : dominant axis detection ────────────────────────────────────

    ''' <summary>
    ''' Walks all solid faces and extracts the geometric descriptor of every
    ''' cylindrical and conical face (axis direction, radius, axial extent).
    ''' Faces are grouped into AxisLineCluster objects: two faces belong to the
    ''' same cluster only when their axis directions are collinear AND their axis
    ''' lines are coaxial (perpendicular distance ≤ COAXIAL_TOLERANCE).
    ''' This prevents parallel-but-offset features (e.g. a radial bore) from being
    ''' merged with the main spindle axis.
    '''
    ''' The volume of each cluster is computed with interval-union arithmetic:
    ''' faces are sorted by radius descending so that OD surfaces claim their axial
    ''' interval first; an inner bore whose interval is already covered contributes
    ''' zero volume.  The cluster with the greatest total volume is returned.
    ''' </summary>
    Private Function GetDominantAxisLine(body As EspritSolids.ISolidBody) As AxisLineCluster
        Dim faces As EspritSolids.ISolidFaces = body.SolidFaces
        Dim clusters As New List(Of AxisLineCluster)()

        For i As Long = 1 To faces.Count
            Dim face As EspritSolids.ISolidFace = faces.Item(i)
            Dim stype As EspritSolids.SolidSurfaceType = face.SolidSurface.SurfaceType

            If stype <> EspritSolids.SolidSurfaceType.geoSurfaceCylinder AndAlso
               stype <> EspritSolids.SolidSurfaceType.geoSurfaceCone Then Continue For

            Try
                Dim direction(2) As Double
                Dim centerBot(2) As Double
                Dim radius As Double = 0
                Dim tMin As Double = 0
                Dim tMax As Double = 0
                Dim angularSpan As Double = 0

                If ExtractCylinderGeometry(face, direction, centerBot, radius, tMin, tMax, angularSpan) Then
                    AccumulateIntoAxisLineCluster(clusters, direction, centerBot, radius, tMin, tMax, angularSpan)
                End If
            Catch
                ' Skip faces whose geometry cannot be read.
            End Try
        Next

        If clusters.Count = 0 Then Return Nothing

        Dim dominant As AxisLineCluster = Nothing
        Dim maxVolume As Double = -1.0

        For Each c As AxisLineCluster In clusters
            Dim vol As Double = ComputeClusterVolume(c)
            If vol > maxVolume Then
                maxVolume = vol
                dominant = c
            End If
        Next

        Return dominant
    End Function

    ' ── Geometry extraction ───────────────────────────────────────────────────

    ''' <summary>
    ''' Extracts the geometric descriptor of a cylindrical or conical solid face
    ''' using parametric surface derivatives — the same technique as
    ''' ExtensionUtilities.AnalyzeCylinder.
    '''
    ''' For a cylinder  P(U,V):
    '''   dP/dU  — tangential; |dP/dU| = radius (exact).
    '''   dP/dV  — axial;      direction = normalize(dP/dV) (exact).
    '''   Circle centers at vMin/vMax: PointAlong − radius × NormalAlong (exact).
    '''
    ''' For a cone P(U,V):
    '''   |dP/dU| at vMid gives the mid-radius (used as representative radius).
    '''   dP/dV is the slant, not the true axis — centers are computed the same
    '''   way as for cylinders (approximation; error is small for shallow tapers).
    '''   The true axis direction is derived from topCenter − botCenter instead.
    '''
    ''' Returns False if the face geometry is degenerate or unreadable.
    ''' </summary>
    Private Function ExtractCylinderGeometry(face As EspritSolids.ISolidFace,
                                              ByRef direction As Double(),
                                              ByRef centerBot As Double(),
                                              ByRef radius As Double,
                                              ByRef tMin As Double,
                                              ByRef tMax As Double,
                                              ByRef angularSpan As Double) As Boolean
        Dim surface As EspritSolids.ISolidSurface = face.SolidSurface
        Dim uMin As Double = 0, uMax As Double = 0, vMin As Double = 0, vMax As Double = 0
        face.FaceLimits(uMin, uMax, vMin, vMax)

        Dim uMid As Double = (uMin + uMax) / 2.0
        Dim vMid As Double = (vMin + vMax) / 2.0

        ' ── Radius = |dP/dU| at the parametric midpoint ──────────────────────
        Dim evalU As Object = surface.Evaluate(uMid, vMid, 1, 0)
        Dim duX As Double = CDbl(evalU(1).X)
        Dim duY As Double = CDbl(evalU(1).Y)
        Dim duZ As Double = CDbl(evalU(1).Z)
        radius = Math.Sqrt(duX * duX + duY * duY + duZ * duZ)
        If radius < 0.001 Then Return False

        ' ── Circle centers and axis direction (method depends on surface type) ──
        Dim botX As Double, botY As Double, botZ As Double
        Dim topX As Double, topY As Double, topZ As Double

        If face.SolidSurface.SurfaceType = EspritSolids.SolidSurfaceType.geoSurfaceCylinder Then
            ' Cylinder: NormalAlong is exactly radial → PointAlong − r×NormalAlong = exact center.
            Dim nBot As IComVector = surface.NormalAlong(uMid, vMin)
            Dim pBot As IComPoint = surface.PointAlong(uMid, vMin)
            botX = pBot.X - radius * nBot.X
            botY = pBot.Y - radius * nBot.Y
            botZ = pBot.Z - radius * nBot.Z

            Dim nTop As IComVector = surface.NormalAlong(uMid, vMax)
            Dim pTop As IComPoint = surface.PointAlong(uMid, vMax)
            topX = pTop.X - radius * nTop.X
            topY = pTop.Y - radius * nTop.Y
            topZ = pTop.Z - radius * nTop.Z

            ' Direction = dP/dV: exactly along the revolution axis for cylinders.
            Dim evalV As Object = surface.Evaluate(uMid, vMid, 0, 1)
            Dim dvX As Double = CDbl(evalV(1).X)
            Dim dvY As Double = CDbl(evalV(1).Y)
            Dim dvZ As Double = CDbl(evalV(1).Z)
            Dim dvLen As Double = Math.Sqrt(dvX * dvX + dvY * dvY + dvZ * dvZ)
            If dvLen < 0.000001 Then Return False
            direction = New Double() {dvX / dvLen, dvY / dvLen, dvZ / dvLen}
        Else
            ' Cone: NormalAlong is tilted by the half-angle, so PointAlong − r×Normal
            ' does NOT give the circle center (error up to r×sin(α) axially).
            ' Fix: the center of a circle is the midpoint of two diametrically opposite
            ' surface points — exact for any surface of revolution (≥180° arc).
            Dim angSpan As Double = uMax - uMin
            Dim uOpp As Double = uMid + angSpan / 2.0   ' point diametrically opposite

            Dim pBot1 As IComPoint = surface.PointAlong(uMid, vMin)
            Dim pBot2 As IComPoint = surface.PointAlong(uOpp, vMin)
            botX = (pBot1.X + pBot2.X) / 2.0
            botY = (pBot1.Y + pBot2.Y) / 2.0
            botZ = (pBot1.Z + pBot2.Z) / 2.0

            Dim pTop1 As IComPoint = surface.PointAlong(uMid, vMax)
            Dim pTop2 As IComPoint = surface.PointAlong(uOpp, vMax)
            topX = (pTop1.X + pTop2.X) / 2.0
            topY = (pTop1.Y + pTop2.Y) / 2.0
            topZ = (pTop1.Z + pTop2.Z) / 2.0

            ' Direction = topCenter − botCenter: true cone axis regardless of half-angle.
            Dim dx As Double = topX - botX
            Dim dy As Double = topY - botY
            Dim dz As Double = topZ - botZ
            Dim dLen As Double = Math.Sqrt(dx * dx + dy * dy + dz * dz)
            If dLen < 0.001 Then Return False
            direction = New Double() {dx / dLen, dy / dLen, dz / dLen}
        End If

        centerBot = New Double() {botX, botY, botZ}

        ' ── Angular span from edge geometry ──────────────────────────────────
        Dim parametricSpan As Double = Math.Min(Math.Abs(uMax - uMin), 2.0 * Math.PI)
        ' If the parametric domain covers a full revolution the surface IS a full
        ' revolution cylinder by definition — trust it directly.  An edge scan can
        ' under-report the span when bounding circles are shared with other features
        ' (e.g. bolt-hole patterns) so only a partial arc appears in the topology.
        If Math.Abs(parametricSpan - 2.0 * Math.PI) < 0.01 Then
            angularSpan = 2.0 * Math.PI
        Else
            angularSpan = ScanEdgesForAngularSpan(face, direction, centerBot, parametricSpan)
        End If

        ' ── Axial projection interval ─────────────────────────────────────────
        ' Project both circle centers onto the axis direction to obtain [tMin, tMax].
        Dim tBot As Double = botX * direction(0) + botY * direction(1) + botZ * direction(2)
        Dim tTop As Double = topX * direction(0) + topY * direction(1) + topZ * direction(2)
        tMin = Math.Min(tBot, tTop)
        tMax = Math.Max(tBot, tTop)

        Return True
    End Function

    ' ── Angular span from edge topology ──────────────────────────────────────

    ''' <summary>
    ''' Scans the loops and edges of a solid face to determine its true angular span
    ''' from the actual bounding arc/circle edges rather than the parametric domain.
    '''
    '''   IComCircle edge aligned with axisDir → full revolution → 2π.
    '''   IComArc edge aligned with axisDir    → sweep = EndAngle − StartAngle,
    '''                                          normalised to (0, 2π].
    '''
    ''' The maximum span found across all aligned circular edges is returned.
    ''' Falls back to <paramref name="fallbackSpan"/> when no circular edge is found.
    '''
    ''' Note: ESPRIT conventionally stores StartAngle ≤ EndAngle for CCW arcs;
    ''' for CW arcs EndAngle &lt; StartAngle, so adding 2π after subtraction gives
    ''' the correct positive sweep.  Angle unit assumed to be radians.
    '''
    ''' Only circular edges whose centre lies on the cylinder's axis line (within
    ''' COAXIAL_TOLERANCE) are accepted.  This filters out inner-loop circles
    ''' (e.g. through-holes in the face) whose normals happen to be collinear with
    ''' the axis but whose centres are offset — otherwise they incorrectly force a
    ''' full 360° span on a partial cylindrical face.
    ''' </summary>
    Private Function ScanEdgesForAngularSpan(face As EspritSolids.ISolidFace,
                                              axisDir As Double(),
                                              axisPoint As Double(),
                                              fallbackSpan As Double) As Double
        Dim maxSpan As Double = -1.0
        Dim loops As EspritSolids.ISolidLoops = face.SolidLoops

        For j As Long = 1 To loops.Count
            Dim looop As EspritSolids.ISolidLoop = loops.Item(j)
            Dim edges As EspritSolids.ISolidEdges = looop.SolidEdges

            For k As Long = 1 To edges.Count
                Dim geo As Object = edges.Item(k).EdgeGeometry
                If geo Is Nothing Then Continue For

                Try
                    ' IMPORTANT: IComArc inherits IComCircle in ESPRIT's COM hierarchy.
                    ' Check IComArc FIRST (more specific) so that partial arcs are measured
                    ' via their StartAngle/EndAngle rather than being treated as full circles.
                    ' Only a true full-revolution circle (IComCircle but NOT IComArc) falls
                    ' through to the second branch and sets maxSpan = 2π.
                    If TypeOf geo Is IComArc Then
                        Dim arc As IComArc = CType(geo, IComArc)
                        Dim n As IComVector = arc.Normal()
                        If n Is Nothing OrElse n.IsZero() Then Continue For
                        n.Normalize()
                        If Math.Abs(n.X * axisDir(0) + n.Y * axisDir(1) + n.Z * axisDir(2)) < COLLINEARITY_THRESHOLD Then Continue For
                        If Not IsOnAxisLine(arc.CenterPoint, axisDir, axisPoint) Then Continue For

                        ' Sweep = EndAngle − StartAngle, normalised to (0, 2π].
                        ' A negative raw value means the arc wraps past 0 (or is CW-stored);
                        ' adding 2π gives the correct positive sweep in both cases.
                        Dim sweep As Double = arc.EndAngle - arc.StartAngle
                        If sweep <= 0 Then sweep += 2.0 * Math.PI
                        If sweep > maxSpan Then maxSpan = sweep

                    ElseIf TypeOf geo Is IComCircle Then
                        ' Only reached for true full-revolution circles (not IComArc).
                        Dim circle As IComCircle = CType(geo, IComCircle)
                        Dim n As IComVector = circle.Normal()
                        If n Is Nothing OrElse n.IsZero() Then Continue For
                        n.Normalize()
                        If Math.Abs(n.X * axisDir(0) + n.Y * axisDir(1) + n.Z * axisDir(2)) < COLLINEARITY_THRESHOLD Then Continue For
                        If Not IsOnAxisLine(circle.CenterPoint, axisDir, axisPoint) Then Continue For
                        maxSpan = 2.0 * Math.PI   ' Full circle — maximum possible span.
                    End If
                Catch
                    ' Skip edges whose geometry cannot be read.
                End Try
            Next
        Next

        Return If(maxSpan > 0.0, maxSpan, fallbackSpan)
    End Function

    ''' <summary>
    ''' Returns True when <paramref name="pt"/> lies within COAXIAL_TOLERANCE of the
    ''' axis line defined by unit direction <paramref name="dir"/> passing through
    ''' <paramref name="refPt"/>.
    ''' </summary>
    Private Function IsOnAxisLine(pt As IComPoint, dir As Double(), refPt As Double()) As Boolean
        If pt Is Nothing Then Return False
        Dim pqX As Double = pt.X - refPt(0)
        Dim pqY As Double = pt.Y - refPt(1)
        Dim pqZ As Double = pt.Z - refPt(2)
        Dim proj As Double = pqX * dir(0) + pqY * dir(1) + pqZ * dir(2)
        Dim perpX As Double = pqX - proj * dir(0)
        Dim perpY As Double = pqY - proj * dir(1)
        Dim perpZ As Double = pqZ - proj * dir(2)
        Return Math.Sqrt(perpX * perpX + perpY * perpY + perpZ * perpZ) <= COAXIAL_TOLERANCE
    End Function

    ' ── Co-cylindrical face merging ───────────────────────────────────────────

    ''' <summary>
    ''' Within a cluster, groups CylinderData entries that share the same radius AND
    ''' the same axial extent (within tolerance).  These represent a single physical
    ''' cylindrical surface split into segments by intersecting flat faces — a common
    ''' millturn scenario where a turned diameter is interrupted by mill pockets.
    '''
    ''' Any group with 2 or more co-cylindrical segments is collapsed into a single
    ''' entry with AngularSpan = 2π: the combined surface is treated as a full
    ''' revolution because the segments together close the circle.
    ''' </summary>
    Private Sub MergeCoCylindricalFaces(cylinders As List(Of CylinderData))
        Const RADIUS_TOL As Double = 0.1   ' mm — radius match tolerance
        Const AXIAL_TOL As Double = 0.5   ' mm — TMin/TMax match tolerance

        Dim consumed(cylinders.Count - 1) As Boolean
        Dim merged As New List(Of CylinderData)()

        For i As Integer = 0 To cylinders.Count - 1
            If consumed(i) Then Continue For
            Dim cyl As CylinderData = cylinders(i)
            consumed(i) = True
            Dim groupCount As Integer = 1

            For k As Integer = i + 1 To cylinders.Count - 1
                If consumed(k) Then Continue For
                Dim other As CylinderData = cylinders(k)
                If Math.Abs(other.Radius - cyl.Radius) <= RADIUS_TOL AndAlso
                   Math.Abs(other.TMin - cyl.TMin) <= AXIAL_TOL AndAlso
                   Math.Abs(other.TMax - cyl.TMax) <= AXIAL_TOL Then
                    groupCount += 1
                    consumed(k) = True
                End If
            Next

            merged.Add(New CylinderData With {
                .Radius = cyl.Radius,
                .TMin = cyl.TMin,
                .TMax = cyl.TMax,
                .AngularSpan = If(groupCount > 1, 2.0 * Math.PI, cyl.AngularSpan)
            })
        Next

        cylinders.Clear()
        cylinders.AddRange(merged)
    End Sub

    ' ── Clustering ────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Adds a cylinder descriptor to the first existing cluster that satisfies BOTH:
    '''   1. The cluster's reference direction is collinear with the new axis
    '''      (|dot| ≥ COLLINEARITY_THRESHOLD).
    '''   2. The cluster's axis line passes within COAXIAL_TOLERANCE of the new
    '''      cylinder's axis line (perpendicular distance between the two lines).
    '''
    ''' If no existing cluster qualifies, a new one is created.
    '''
    ''' When the new axis is anti-parallel to the cluster's RefDirection (dot &lt; 0),
    ''' the axial projection is negated so all intervals in a cluster share the same
    ''' orientation and can be compared directly during interval-union arithmetic.
    ''' </summary>
    Private Sub AccumulateIntoAxisLineCluster(clusters As List(Of AxisLineCluster),
                                               direction As Double(),
                                               centerBot As Double(),
                                               radius As Double,
                                               tMin As Double,
                                               tMax As Double,
                                               angularSpan As Double)
        For Each c As AxisLineCluster In clusters

            ' ── Condition 1: parallel directions ─────────────────────────────
            Dim dot As Double = c.RefDirection(0) * direction(0) +
                                c.RefDirection(1) * direction(1) +
                                c.RefDirection(2) * direction(2)
            If Math.Abs(dot) < COLLINEARITY_THRESHOLD Then Continue For

            ' ── Condition 2: coaxial lines (perpendicular distance) ───────────
            ' Vector from c.RefPoint to the new center.
            Dim pqX As Double = centerBot(0) - c.RefPoint(0)
            Dim pqY As Double = centerBot(1) - c.RefPoint(1)
            Dim pqZ As Double = centerBot(2) - c.RefPoint(2)
            ' Project onto cluster direction, then subtract to get perpendicular component.
            Dim proj As Double = pqX * c.RefDirection(0) +
                                  pqY * c.RefDirection(1) +
                                  pqZ * c.RefDirection(2)
            Dim perpX As Double = pqX - proj * c.RefDirection(0)
            Dim perpY As Double = pqY - proj * c.RefDirection(1)
            Dim perpZ As Double = pqZ - proj * c.RefDirection(2)
            If Math.Sqrt(perpX * perpX + perpY * perpY + perpZ * perpZ) > COAXIAL_TOLERANCE Then Continue For

            ' ── Match: normalise projection sign to the cluster direction ─────
            ' If anti-parallel (dot < 0), negate tMin/tMax so that all intervals
            ' in the cluster are expressed along the same reference direction.
            Dim adjMin As Double = If(dot >= 0, tMin, -tMax)
            Dim adjMax As Double = If(dot >= 0, tMax, -tMin)

            c.Cylinders.Add(New CylinderData With {
                .Radius = radius,
                .TMin = adjMin,
                .TMax = adjMax,
                .AngularSpan = angularSpan
            })
            Return
        Next

        ' No matching cluster — create a new one.
        Dim nc As New AxisLineCluster()
        nc.RefDirection = direction
        nc.RefPoint = centerBot
        nc.Cylinders.Add(New CylinderData With {
            .Radius = radius,
            .TMin = tMin,
            .TMax = tMax,
            .AngularSpan = angularSpan
        })
        clusters.Add(nc)
    End Sub

    ' ── Volume computation ────────────────────────────────────────────────────

    ''' <summary>
    ''' Computes the total accumulated volume of a coaxial cluster using
    ''' interval-union arithmetic.
    '''
    ''' Faces are processed in decreasing-radius order so that outer surfaces (OD)
    ''' claim their axial interval before inner bores (ID) are evaluated.
    ''' A bore whose interval falls entirely within an already-covered range
    ''' contributes zero volume to the total (it adds no new axial "column").
    ''' </summary>
    Private Function ComputeClusterVolume(cluster As AxisLineCluster) As Double
        ' Merge co-cylindrical face segments (same R + same axial extent) into one
        ' full-revolution entry before computing volume.
        MergeCoCylindricalFaces(cluster.Cylinders)

        ' Sort descending by radius: OD surfaces processed first.
        cluster.Cylinders.Sort(Function(a, b) b.Radius.CompareTo(a.Radius))

        Dim covered As New List(Of Interval)()
        Dim total As Double = 0.0

        For Each cyl As CylinderData In cluster.Cylinders
            Dim effective As Double = ComputeUncoveredLength(cyl.TMin, cyl.TMax, covered)
            ' Volume of a cylindrical sector = (θ/2) × r² × L
            ' For a full revolution (θ = 2π): equals the standard π×r²×L.
            ' For a partial arc (θ < 2π): correctly penalises incomplete surfaces.
            total += (cyl.AngularSpan / 2.0) * cyl.Radius * cyl.Radius * effective
            If effective > 0.0 Then MergeInterval(covered, cyl.TMin, cyl.TMax)
        Next

        Return total
    End Function

    ''' <summary>
    ''' Returns the total length of [tMin, tMax] not yet covered by any interval
    ''' in <paramref name="covered"/>.
    ''' </summary>
    Private Function ComputeUncoveredLength(tMin As Double,
                                             tMax As Double,
                                             covered As List(Of Interval)) As Double
        If tMin >= tMax Then Return 0.0

        ' Start with the full segment; subtract each already-covered interval.
        Dim remaining As New List(Of Interval) From {New Interval(tMin, tMax)}

        For Each cov As Interval In covered
            Dim newList As New List(Of Interval)()
            For Each seg As Interval In remaining
                If cov.TMax <= seg.TMin OrElse cov.TMin >= seg.TMax Then
                    newList.Add(seg)                            ' no overlap — keep whole segment
                Else
                    If seg.TMin < cov.TMin Then newList.Add(New Interval(seg.TMin, cov.TMin))  ' left remnant
                    If seg.TMax > cov.TMax Then newList.Add(New Interval(cov.TMax, seg.TMax))  ' right remnant
                End If
            Next
            remaining = newList
            If remaining.Count = 0 Then Return 0.0            ' short-circuit
        Next

        Dim length As Double = 0.0
        For Each seg As Interval In remaining
            length += seg.TMax - seg.TMin
        Next
        Return length
    End Function

    ''' <summary>
    ''' Inserts [tMin, tMax] into <paramref name="covered"/> and merges overlapping
    ''' intervals so the list always holds the minimal set of disjoint intervals.
    ''' </summary>
    Private Sub MergeInterval(covered As List(Of Interval), tMin As Double, tMax As Double)
        covered.Add(New Interval(tMin, tMax))
        covered.Sort(Function(a, b) a.TMin.CompareTo(b.TMin))

        Dim merged As New List(Of Interval)()
        For Each seg As Interval In covered
            If merged.Count = 0 OrElse seg.TMin > merged(merged.Count - 1).TMax Then
                merged.Add(New Interval(seg.TMin, seg.TMax))
            Else
                merged(merged.Count - 1).TMax = Math.Max(merged(merged.Count - 1).TMax, seg.TMax)
            End If
        Next

        covered.Clear()
        covered.AddRange(merged)
    End Sub

    ' ── Phase 2 : best cylinder face selection ────────────────────────────────

    ''' <summary>
    ''' Among all geoSurfaceCylinder faces whose axis is collinear with refAxis,
    ''' returns the one with the highest score = Radius × Height.
    '''
    ''' Height is the distance between the centres of the two bounding circle edges
    ''' of the face. This equals the axial length of the cylinder regardless of its
    ''' current orientation in space.
    '''
    ''' Using Radius × Height (proportional to actual surface area) rather than
    ''' Radius alone prevents a wide shallow pocket from outscoring the long main OD.
    ''' Falls back to Radius alone when only one bounding circle is found.
    ''' </summary>
    Private Function FindBestCylinderFace(body As EspritSolids.ISolidBody,
                                           refAxis As Double()) As EspritSolids.ISolidFace
        Dim faces As EspritSolids.ISolidFaces = body.SolidFaces
        Dim bestFace As EspritSolids.ISolidFace = Nothing
        Dim bestScore As Double = -1.0

        For i As Long = 1 To faces.Count
            Dim face As EspritSolids.ISolidFace = faces.Item(i)
            If face.SolidSurface.SurfaceType <> EspritSolids.SolidSurfaceType.geoSurfaceCylinder Then
                Continue For
            End If

            ' Collect the bounding circle edges of this cylindrical face.
            Dim radius As Double = -1.0
            Dim center1 As IComPoint = Nothing
            Dim center2 As IComPoint = Nothing
            Dim loops As EspritSolids.ISolidLoops = face.SolidLoops

            For j As Long = 1 To loops.Count
                Dim looop As EspritSolids.ISolidLoop = loops.Item(j)
                Dim edges As EspritSolids.ISolidEdges = looop.SolidEdges

                For k As Long = 1 To edges.Count
                    Dim geo As Object = edges.Item(k).EdgeGeometry
                    If geo Is Nothing Then Continue For
                    Try
                        If Not TypeOf geo Is IComCircle Then Continue For
                        Dim circle As IComCircle = CType(geo, IComCircle)

                        ' Verify this circle's axis is collinear with the dominant axis.
                        Dim n As IComVector = circle.Normal()
                        If n Is Nothing OrElse n.IsZero() Then Continue For
                        n.Normalize()
                        Dim dot As Double = Math.Abs(n.X * refAxis(0) +
                                                      n.Y * refAxis(1) +
                                                      n.Z * refAxis(2))
                        If dot < ALIGNMENT_THRESHOLD Then Continue For

                        radius = circle.Radius
                        If center1 Is Nothing Then
                            center1 = circle.CenterPoint
                        Else
                            center2 = circle.CenterPoint
                        End If
                    Catch
                    End Try
                Next
            Next

            If radius <= 0 Then Continue For

            ' Score = Radius × Height (height = distance between the two circle centres).
            Dim score As Double
            If center1 IsNot Nothing AndAlso center2 IsNot Nothing Then
                Dim dx As Double = center2.X - center1.X
                Dim dy As Double = center2.Y - center1.Y
                Dim dz As Double = center2.Z - center1.Z
                score = radius * Math.Sqrt(dx * dx + dy * dy + dz * dz)
            Else
                score = radius
            End If

            If score > bestScore Then
                bestScore = score
                bestFace = face
            End If
        Next

        Return bestFace
    End Function

    ' ── Alignment ─────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Rotates all document objects to align the given unit axis vector with the world Z axis.
    ''' Uses SelectionSet.Rotate — works for any surface type, no face selection required.
    '''
    ''' Cases:
    '''   axis ≈ +Z : already aligned, no rotation.
    '''   axis ≈ -Z : 180° around X through origin.
    '''   otherwise  : rotation axis = axis × Z = (axis.Y, -axis.X, 0)
    '''                angle = acos(axis · Z)  [radians]
    ''' </summary>
    Private Sub AlignAxisToZ(doc As ESPRIT.Document, axis As Double())
        Dim dotZ As Double = axis(2) ' dot product with (0, 0, 1)

        If Math.Abs(dotZ) > 0.9999 Then
            If dotZ < 0 Then
                RotateAllDocumentObjects(doc, 1.0, 0.0, 0.0, Math.PI)
            End If
            Return
        End If

        Dim rx As Double = axis(1)
        Dim ry As Double = -axis(0)
        Dim rLen As Double = Math.Sqrt(rx * rx + ry * ry)
        If rLen < 0.000001 Then Return

        Dim angle As Double = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dotZ)))
        RotateAllDocumentObjects(doc, rx / rLen, ry / rLen, 0.0, angle)
    End Sub

    ' ── Document rotation ─────────────────────────────────────────────────────

    ''' <summary>
    ''' Adds all document objects (except the XYZ work coordinate) to a temporary
    ''' SelectionSet and rotates them by the given angle (radians) around a unit axis
    ''' through the world origin.
    ''' </summary>
    Private Sub RotateAllDocumentObjects(doc As ESPRIT.Document,
                                          dx As Double,
                                          dy As Double,
                                          dz As Double,
                                          angle As Double)
        Try
            doc.SelectionSets.Remove(TEMP_SS_NAME)
        Catch
        End Try

        Dim ss As ESPRIT.SelectionSet = doc.SelectionSets.Add(TEMP_SS_NAME)
        ss.RemoveAll()

        Try
            For Each obj As ESPRIT.GraphicObject In doc.GraphicsCollection
                Try
                    If obj.GraphicObjectType = EspritConstants.espGraphicObjectType.espWorkCoordinate Then
                        Dim wc = CType(obj, ESPRIT.WorkCoordinate)
                        If wc.Name = "XYZ" Then Continue For
                    End If
                    ss.Add(obj)
                Catch
                End Try
            Next

            Dim origin As ESPRIT.Point = doc.GetPoint(0, 0, 0)
            Dim rotAxis As ESPRIT.Line = doc.GetLine(origin, dx, dy, dz)
            Try
                ss.Rotate(rotAxis, angle, 0)
            Finally
                Try
                    doc.SelectionSets.Remove(TEMP_SS_NAME)
                Catch
                End Try
            End Try
        Catch
        End Try
    End Sub

    ' ── Flip ─────────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Flips all document objects 180° around the Y axis at the world origin.
    ''' </summary>
    Private Sub FlipSolid180(doc As ESPRIT.Document, solid As ESPRIT.ISolid)
        MoveP0ToTopZ(doc, solid, toMinus:=True)
        RotateAllDocumentObjects(doc, 0.0, 1.0, 0.0, Math.PI)
    End Sub

    ' ── P0 placement ─────────────────────────────────────────────────────────

    ''' <summary>
    ''' Computes the bounding box of the solid body and moves P0 to (0, 0, targetZ).
    ''' By default targetZ = maxZ (top face). Pass toMinus:=True to use minZ instead.
    ''' </summary>
    Private Sub MoveP0ToTopZ(doc As ESPRIT.Document, solid As ESPRIT.ISolid,
                              Optional toMinus As Boolean = False)
        Try
            Dim body As EspritSolids.ISolidBody = CType(solid.SolidBody, EspritSolids.ISolidBody)
            Dim minPt As IComPoint = Nothing
            Dim maxPt As IComPoint = Nothing

            Dim matrix As IComMatrix =
                CType(doc.Planes.Item("XYZ").GlobalToLocalMatrix, IComMatrix)

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
