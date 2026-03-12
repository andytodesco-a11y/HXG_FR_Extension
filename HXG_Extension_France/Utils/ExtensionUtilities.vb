Imports EspritGeometryBase
Imports EspritSolids

''' <summary>
''' Shared utility methods callable from any feature in the extension.
''' </summary>
Public Module ExtensionUtilities

    Private Const MSG_SOURCE As String = "AnalyzeFace"
    Private Const SELECTION_SOURCE As String = "SolidBodySelection"
    Private Const TEMP_SS_NAME As String = "_HXG_TempRotate"

    Private _app As ESPRIT.Application

    ' ── Initialisation ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Must be called once from Main.Connect before any utility function is used.
    ''' </summary>
    Public Sub Initialize(app As ESPRIT.Application)
        _app = app
    End Sub

    ' ── Public API ────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Returns the ISolidBody of the single selected element.
    ''' Accepts ISolid, ISolidFace, ISolidLoop, or ISolidEdge.
    ''' Logs a warning and returns Nothing on failure.
    ''' </summary>
    Public Function GetSelectedSolidBody(doc As ESPRIT.Document) As EspritSolids.ISolidBody
        If doc.Group.Count = 0 Then
            LogWarning(SELECTION_SOURCE, "No element selected. Please select a solid, face, loop, or edge.")
            Return Nothing
        End If
        If doc.Group.Count > 1 Then
            LogWarning(SELECTION_SOURCE, "Multiple elements selected. Please select a single element only.")
            Return Nothing
        End If
        Dim item As Object = doc.Group.Item(1)
        Try
            Dim body As EspritSolids.ISolidBody = TryCast(item.SolidBody, EspritSolids.ISolidBody)
            If body IsNot Nothing Then Return body
        Catch
        End Try
        LogWarning(SELECTION_SOURCE, "The selected element is not a solid, face, loop, or edge.")
        Return Nothing
    End Function

    ''' <summary>
    ''' Analyzes a SolidFace and reports its geometric properties to the EventWindow.
    ''' For a plane   : surface type, area, normal vector.
    ''' For a cylinder: surface type, area, axis direction (UVW), bottom center (XYZ),
    '''                 top center (XYZ), and diameter.
    ''' </summary>
    ''' <param name="face">The solid face to analyze.</param>
    ''' <param name="app">The ESPRIT application (used to write messages).</param>
    Public Sub AnalyzeFace(face As ISolidFace, app As ESPRIT.Application)
        If face Is Nothing OrElse app Is Nothing Then Return

        Try
            Dim surface As ISolidSurface = face.SolidSurface
            Dim typeCode As SolidSurfaceType = surface.SurfaceType

            LogInfo(app, "--- Face Analysis ---")
            LogInfo(app, $"Surface type: {GetSurfaceTypeName(typeCode)}")

            Select Case typeCode
                Case SolidSurfaceType.geoSurfacePlane
                    AnalyzePlane(face, surface, app)
                Case SolidSurfaceType.geoSurfaceCylinder
                    AnalyzeCylinder(face, surface, app)
                Case Else
                    LogInfo(app, "  (No detailed analysis available for this surface type.)")
            End Select

        Catch ex As Exception
            app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeError,
                MSG_SOURCE,
                $"Error during face analysis: {ex.Message}")
        End Try
    End Sub

    ' ── Plane analysis ────────────────────────────────────────────────────────

    Private Sub AnalyzePlane(face As ISolidFace, surface As ISolidSurface, app As ESPRIT.Application)
        Dim uMin As Double = 0, uMax As Double = 0, vMin As Double = 0, vMax As Double = 0
        face.FaceLimits(uMin, uMax, vMin, vMax)

        Dim uMid As Double = (uMin + uMax) / 2.0
        Dim vMid As Double = (vMin + vMax) / 2.0

        ' Normal at the parametric centre of the face
        Dim normal As IComVector = surface.NormalAlong(uMid, vMid)

        ' Area = |dP/dU x dP/dV| * (uMax - uMin) * (vMax - vMin)
        ' Exact for rectangular faces; approximate for trimmed non-rectangular faces.
        Dim areaElem As Double = ComputeAreaElement(surface, uMid, vMid)
        Dim area As Double = areaElem * (uMax - uMin) * (vMax - vMin)

        LogInfo(app, $"  Area  : {area:F4} mm²")
        LogInfo(app, $"  Normal: X={normal.X:F4}  Y={normal.Y:F4}  Z={normal.Z:F4}")
    End Sub

    ' ── Cylinder analysis ─────────────────────────────────────────────────────

    Private Sub AnalyzeCylinder(face As ISolidFace, surface As ISolidSurface, app As ESPRIT.Application)
        Dim uMin As Double = 0, uMax As Double = 0, vMin As Double = 0, vMax As Double = 0
        face.FaceLimits(uMin, uMax, vMin, vMax)

        Dim uMid As Double = (uMin + uMax) / 2.0
        Dim vMid As Double = (vMin + vMax) / 2.0

        ' --- Radius: magnitude of dP/dU at the parametric midpoint ---
        ' For a cylinder P(U,V): dP/dU is always tangential and |dP/dU| = radius.
        Dim evalU As Object = surface.Evaluate(uMid, vMid, 1, 0)
        Dim duX As Double = CDbl(evalU(1).X)
        Dim duY As Double = CDbl(evalU(1).Y)
        Dim duZ As Double = CDbl(evalU(1).Z)
        Dim radius As Double = Math.Sqrt(duX * duX + duY * duY + duZ * duZ)

        ' --- Axis direction: dP/dV at the parametric midpoint ---
        ' For a cylinder, dP/dV is constant and aligned with the revolution axis.
        Dim evalV As Object = surface.Evaluate(uMid, vMid, 0, 1)
        Dim dvX As Double = CDbl(evalV(1).X)
        Dim dvY As Double = CDbl(evalV(1).Y)
        Dim dvZ As Double = CDbl(evalV(1).Z)
        Dim dvLen As Double = Math.Sqrt(dvX * dvX + dvY * dvY + dvZ * dvZ)

        ' Normalised axis vector for display
        Dim axX As Double = 0, axY As Double = 0, axZ As Double = 0
        If dvLen > 0 Then
            axX = dvX / dvLen : axY = dvY / dvLen : axZ = dvZ / dvLen
        End If

        ' --- Area = R * |dP/dV| * angularSpan * vSpan ---
        Dim area As Double = radius * dvLen * (uMax - uMin) * Math.Abs(vMax - vMin)

        ' --- Bottom circle centre: surface point - R * outward normal at vMin ---
        Dim nBot As IComVector = surface.NormalAlong(uMid, vMin)
        Dim pBot As IComPoint = surface.PointAlong(uMid, vMin)
        Dim botX As Double = pBot.X - radius * nBot.X
        Dim botY As Double = pBot.Y - radius * nBot.Y
        Dim botZ As Double = pBot.Z - radius * nBot.Z

        ' --- Top circle centre: surface point - R * outward normal at vMax ---
        Dim nTop As IComVector = surface.NormalAlong(uMid, vMax)
        Dim pTop As IComPoint = surface.PointAlong(uMid, vMax)
        Dim topX As Double = pTop.X - radius * nTop.X
        Dim topY As Double = pTop.Y - radius * nTop.Y
        Dim topZ As Double = pTop.Z - radius * nTop.Z

        LogInfo(app, $"  Area        : {area:F4} mm²")
        LogInfo(app, $"  Axis (UVW)  : X={axX:F4}  Y={axY:F4}  Z={axZ:F4}")
        LogInfo(app, $"  Bottom (XYZ): X={botX:F4}  Y={botY:F4}  Z={botZ:F4}")
        LogInfo(app, $"  Top    (XYZ): X={topX:F4}  Y={topY:F4}  Z={topZ:F4}")
        LogInfo(app, $"  Diameter    : {2.0 * radius:F4} mm")
    End Sub

    ' ── Geometry helpers ──────────────────────────────────────────────────────

    ''' <summary>
    ''' Returns the magnitude of the cross product dP/dU x dP/dV at (u, v),
    ''' which equals the area element of the surface at that parametric point.
    ''' </summary>
    Private Function ComputeAreaElement(surface As ISolidSurface,
                                        u As Double,
                                        v As Double) As Double
        Dim evalU As Object = surface.Evaluate(u, v, 1, 0)
        Dim duX As Double = CDbl(evalU(1).X)
        Dim duY As Double = CDbl(evalU(1).Y)
        Dim duZ As Double = CDbl(evalU(1).Z)

        Dim evalV As Object = surface.Evaluate(u, v, 0, 1)
        Dim dvX As Double = CDbl(evalV(1).X)
        Dim dvY As Double = CDbl(evalV(1).Y)
        Dim dvZ As Double = CDbl(evalV(1).Z)

        ' Cross product components
        Dim cpX As Double = duY * dvZ - duZ * dvY
        Dim cpY As Double = duZ * dvX - duX * dvZ
        Dim cpZ As Double = duX * dvY - duY * dvX

        Return Math.Sqrt(cpX * cpX + cpY * cpY + cpZ * cpZ)
    End Function

    ' ── Surface type name ─────────────────────────────────────────────────────

    Private Function GetSurfaceTypeName(typeCode As SolidSurfaceType) As String
        Select Case typeCode
            Case SolidSurfaceType.geoSurfaceUnknown : Return "Unknown"
            Case SolidSurfaceType.geoSurfacePlane : Return "Plane"
            Case SolidSurfaceType.geoSurfaceCylinder : Return "Cylinder"
            Case SolidSurfaceType.geoSurfaceCone : Return "Cone"
            Case SolidSurfaceType.geoSurfaceSphere : Return "Sphere"
            Case SolidSurfaceType.geoSurfaceTorus : Return "Torus"
            Case SolidSurfaceType.geoSurfaceNurb : Return "Nurb"
            Case SolidSurfaceType.geoSurfaceBlend : Return "Blend"
            Case SolidSurfaceType.geoSurfaceSwept : Return "Swept"
            Case SolidSurfaceType.geoSurfaceSpun : Return "Spun"
            Case SolidSurfaceType.geoSurfaceOffset : Return "Offset"
            Case Else : Return $"Type({CInt(typeCode)})"
        End Select
    End Function

    ' ── Align X angle extraction ──────────────────────────────────────────────

    ''' <summary>
    ''' Returns the XY angle (radians, measured from +X) of the reference direction
    ''' extracted from the selected item, for use with an "Align X" rotation around Z.
    '''
    ''' Supported types:
    '''   - ESPRIT.Point                   : position projected on XY.
    '''   - ISolidFace (plane/cylinder/cone): delegates to GetAlignXAngleFromFace.
    '''   - ISolidEdge (IComLine)           : edge direction projected on XY,
    '''                                       normalised to [-90°, +90°].
    '''   - ISolidEdge (IComArc/IComCircle) : arc/circle centre projected on XY.
    '''
    ''' Returns Double.NaN when the type is unsupported or the XY projection is
    ''' degenerate (distance from origin &lt; 0.01 mm).
    ''' </summary>
    Public Function GetAlignXAngle(item As Object) As Double
        Try
            If TypeOf item Is ESPRIT.Point Then
                Dim pt As ESPRIT.Point = CType(item, ESPRIT.Point)
                Dim projLen As Double = Math.Sqrt(pt.X * pt.X + pt.Y * pt.Y)
                If projLen < 0.01 Then Return Double.NaN
                Return Math.Atan2(pt.Y, pt.X)
            End If

            If TypeOf item Is EspritSolids.ISolidFace Then
                Return GetAlignXAngleFromFace(CType(item, EspritSolids.ISolidFace))
            End If

            If TypeOf item Is EspritSolids.ISolidEdge Then
                Return GetAlignXAngleFromEdge(CType(item, EspritSolids.ISolidEdge))
            End If
        Catch
        End Try
        Return Double.NaN
    End Function

    ''' <summary>
    ''' Returns True when the edge geometry type is supported by GetAlignXAngle
    ''' (IComLine, IComArc, or IComCircle).  Returns False for splines, ellipses, etc.
    ''' Callers should check this before calling GetAlignXAngle on an ISolidEdge
    ''' in order to report a meaningful error for unsupported edge types.
    ''' </summary>
    Public Function IsAlignXEdgeSupported(edge As EspritSolids.ISolidEdge) As Boolean
        Try
            Dim geo As Object = edge.EdgeGeometry
            Return geo IsNot Nothing AndAlso
                   (TypeOf geo Is IComLine OrElse
                    TypeOf geo Is IComArc OrElse
                    TypeOf geo Is IComCircle)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Returns the XY angle of the face reference direction projected onto the XY plane.
    '''   Plane         : face normal projected on XY → angle to X.
    '''   Cylinder/Cone : axis direction projected on XY → angle to X.
    '''                   Falls back to axis-centre XY position when the axis is
    '''                   nearly vertical (|XY projection| &lt; 0.01, i.e. axis ≈ Z).
    ''' Returns Double.NaN for unsupported face types or degenerate geometry.
    ''' </summary>
    Public Function GetAlignXAngleFromFace(face As EspritSolids.ISolidFace) As Double
        Try
            Dim surf As EspritSolids.ISolidSurface = face.SolidSurface
            Dim uMin As Double = 0, uMax As Double = 0, vMin As Double = 0, vMax As Double = 0
            face.FaceLimits(uMin, uMax, vMin, vMax)

            Dim nx As Double, ny As Double

            Select Case surf.SurfaceType
                Case EspritSolids.SolidSurfaceType.geoSurfacePlane
                    Dim uMid As Double = (uMin + uMax) / 2.0
                    Dim vMid As Double = (vMin + vMax) / 2.0
                    Dim n As IComVector = surf.NormalAlong(uMid, vMid)
                    If n Is Nothing OrElse n.IsZero() Then Return Double.NaN
                    nx = n.X : ny = n.Y

                Case EspritSolids.SolidSurfaceType.geoSurfaceCylinder,
                     EspritSolids.SolidSurfaceType.geoSurfaceCone
                    Dim dirX As Double, dirY As Double
                    Dim cenBotX As Double, cenBotY As Double
                    If Not ExtractCylinderAxisAndCenter(surf, uMin, uMax, vMin, vMax,
                                                        dirX, dirY, cenBotX, cenBotY) Then
                        Return Double.NaN
                    End If
                    nx = dirX : ny = dirY
                    ' Axis nearly along Z — fall back to cylinder centre XY position
                    ' (handles off-axis bores / bosses whose centre defines the angle).
                    If Math.Sqrt(nx * nx + ny * ny) < 0.01 Then
                        nx = cenBotX : ny = cenBotY
                    End If

                Case Else
                    Return Double.NaN
            End Select

            Dim projLen As Double = Math.Sqrt(nx * nx + ny * ny)
            If projLen < 0.01 Then Return Double.NaN
            Return Math.Atan2(ny, nx)
        Catch
            Return Double.NaN
        End Try
    End Function

    ''' <summary>
    ''' Extracts the axis direction (XY components) and bottom-circle centre (XY)
    ''' of a cylindrical or conical surface.  Used for Align X angle computation.
    '''
    '''   Cylinder : direction = dP/dV (exact); centre via PointAlong − r×NormalAlong.
    '''   Cone     : direction from topCenter − botCenter; centres via midpoint method.
    '''
    ''' Returns False if the geometry is degenerate.
    ''' </summary>
    Private Function ExtractCylinderAxisAndCenter(surf As EspritSolids.ISolidSurface,
                                                   uMin As Double, uMax As Double,
                                                   vMin As Double, vMax As Double,
                                                   ByRef dirX As Double, ByRef dirY As Double,
                                                   ByRef cenBotX As Double,
                                                   ByRef cenBotY As Double) As Boolean
        Dim uMid As Double = (uMin + uMax) / 2.0
        Dim vMid As Double = (vMin + vMax) / 2.0

        ' Radius = |dP/dU| at parametric midpoint.
        Dim evalU As Object = surf.Evaluate(uMid, vMid, 1, 0)
        Dim duX As Double = CDbl(evalU(1).X)
        Dim duY As Double = CDbl(evalU(1).Y)
        Dim duZ As Double = CDbl(evalU(1).Z)
        Dim radius As Double = Math.Sqrt(duX * duX + duY * duY + duZ * duZ)
        If radius < 0.001 Then Return False

        If surf.SurfaceType = EspritSolids.SolidSurfaceType.geoSurfaceCylinder Then
            ' Axis direction = dP/dV (exact for cylinders).
            Dim evalV As Object = surf.Evaluate(uMid, vMid, 0, 1)
            Dim dvX As Double = CDbl(evalV(1).X)
            Dim dvY As Double = CDbl(evalV(1).Y)
            Dim dvZ As Double = CDbl(evalV(1).Z)
            Dim dvLen As Double = Math.Sqrt(dvX * dvX + dvY * dvY + dvZ * dvZ)
            If dvLen < 0.000001 Then Return False
            dirX = dvX / dvLen
            dirY = dvY / dvLen

            ' Bottom circle centre = PointAlong − r × NormalAlong (exact for cylinder).
            Dim nBot As IComVector = surf.NormalAlong(uMid, vMin)
            Dim pBot As IComPoint = surf.PointAlong(uMid, vMin)
            cenBotX = pBot.X - radius * nBot.X
            cenBotY = pBot.Y - radius * nBot.Y
        Else
            ' Cone: derive axis from topCenter − botCenter; use midpoint method for centers.
            Dim angSpan As Double = uMax - uMin
            Dim uOpp As Double = uMid + angSpan / 2.0

            Dim pBot1 As IComPoint = surf.PointAlong(uMid, vMin)
            Dim pBot2 As IComPoint = surf.PointAlong(uOpp, vMin)
            cenBotX = (pBot1.X + pBot2.X) / 2.0
            cenBotY = (pBot1.Y + pBot2.Y) / 2.0

            Dim pTop1 As IComPoint = surf.PointAlong(uMid, vMax)
            Dim pTop2 As IComPoint = surf.PointAlong(uOpp, vMax)
            Dim topX As Double = (pTop1.X + pTop2.X) / 2.0
            Dim topY As Double = (pTop1.Y + pTop2.Y) / 2.0
            Dim topZ As Double = (pTop1.Z + pTop2.Z) / 2.0
            Dim botZ As Double = (pBot1.Z + pBot2.Z) / 2.0

            Dim dx As Double = topX - cenBotX
            Dim dy As Double = topY - cenBotY
            Dim dz As Double = topZ - botZ
            Dim dLen As Double = Math.Sqrt(dx * dx + dy * dy + dz * dz)
            If dLen < 0.001 Then Return False
            dirX = dx / dLen
            dirY = dy / dLen
        End If

        Return True
    End Function

    ''' <summary>
    ''' Returns the XY angle of the edge reference direction.
    '''   IComLine              : edge direction projected on XY,
    '''                           normalised to [-90°, +90°] (no direction preference).
    '''   IComArc / IComCircle  : arc/circle centre projected on XY.
    ''' Returns Double.NaN for other edge types or degenerate geometry.
    ''' </summary>
    Private Function GetAlignXAngleFromEdge(edge As EspritSolids.ISolidEdge) As Double
        Try
            Dim geo As Object = edge.EdgeGeometry
            If geo Is Nothing Then Return Double.NaN

            If TypeOf geo Is IComLine Then
                Dim bounded As ComGeoBounded = TryCast(geo, ComGeoBounded)
                If bounded Is Nothing OrElse bounded.Length < 0.001 Then Return Double.NaN
                Dim p0 As IComPoint = bounded.PointAlong(0)
                Dim p1 As IComPoint = bounded.PointAlong(bounded.Length)
                If p0 Is Nothing OrElse p1 Is Nothing Then Return Double.NaN
                Dim dx As Double = p1.X - p0.X
                Dim dy As Double = p1.Y - p0.Y
                Dim projLen As Double = Math.Sqrt(dx * dx + dy * dy)
                If projLen < 0.001 Then Return Double.NaN
                ' Normalise to [-90°, +90°]: a line has no direction preference.
                Dim angle As Double = Math.Atan2(dy, dx)
                If angle > Math.PI / 2.0 Then angle -= Math.PI
                If angle < -Math.PI / 2.0 Then angle += Math.PI
                Return angle

            ElseIf TypeOf geo Is IComArc Then
                ' IComArc inherits IComCircle — check IComArc first (partial arc).
                Dim arc As IComArc = CType(geo, IComArc)
                Dim center As IComPoint = arc.CenterPoint
                If center Is Nothing Then Return Double.NaN
                Dim projLen As Double = Math.Sqrt(center.X * center.X + center.Y * center.Y)
                If projLen < 0.01 Then Return Double.NaN
                Return Math.Atan2(center.Y, center.X)

            ElseIf TypeOf geo Is IComCircle Then
                Dim circle As IComCircle = CType(geo, IComCircle)
                Dim center As IComPoint = circle.CenterPoint
                If center Is Nothing Then Return Double.NaN
                Dim projLen As Double = Math.Sqrt(center.X * center.X + center.Y * center.Y)
                If projLen < 0.01 Then Return Double.NaN
                Return Math.Atan2(center.Y, center.X)
            End If
        Catch
        End Try
        Return Double.NaN
    End Function

    ' ── Document rotation ─────────────────────────────────────────────────────

    ''' <summary>
    ''' Adds all document objects (except the XYZ work coordinate and system
    ''' geometry with negative keys) to a temporary SelectionSet and rotates them
    ''' by <paramref name="angle"/> radians around the unit axis (dx, dy, dz)
    ''' through the world origin.
    ''' </summary>
    Public Sub RotateAllDocumentObjects(doc As ESPRIT.Document,
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
                    ElseIf obj.GraphicObjectType = EspritConstants.espGraphicObjectType.espLine Then
                        Dim ln = CType(obj, ESPRIT.Line)
                        If Val(ln.Key) < 0 Then Continue For
                    ElseIf obj.GraphicObjectType = EspritConstants.espGraphicObjectType.espPoint Then
                        Dim pt = CType(obj, ESPRIT.Point)
                        If Val(pt.Key) < 0 Then Continue For
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

    ' ── Align X ───────────────────────────────────────────────────────────────

    ''' <summary>
    ''' Rotates all document objects around Z to align the selected reference
    ''' with the +X axis.
    '''
    ''' Supported selection (first valid item in the group wins):
    '''   Point              — the point lands on +X.
    '''   Line edge          — the edge becomes parallel to X.
    '''   Arc / circle edge  — the arc/circle centre lands on +X.
    '''   Other edge         — error: unsupported geometry type.
    '''   Cylindrical/conical face — the axis direction points toward +X
    '''                              (falls back to centre XY when axis ≈ Z).
    '''   Planar face        — the face normal points toward +X.
    '''
    ''' When the reference already sits at a cardinal angle (0°, 90°, 180°, 270°),
    ''' steps 90° CCW instead of doing nothing.
    ''' </summary>
    Public Sub AlignX()
        Dim doc As ESPRIT.Document = _app.Document
        If doc Is Nothing Then LogWarning("AlignX", "No document is open.") : Return

        If doc.Group.Count = 0 Then
            LogWarning("AlignX", "Align X: no selection. Select a point, a line/arc/circle edge, or a planar/cylindrical face.")
            Return
        End If

        Dim refAngle As Double = Double.NaN

        For i As Long = 1 To doc.Group.Count
            Dim item As Object = doc.Group.Item(i)

            ' For edges, validate geometry type before computing the angle.
            If TypeOf item Is EspritSolids.ISolidEdge Then
                If Not IsAlignXEdgeSupported(CType(item, EspritSolids.ISolidEdge)) Then
                    LogWarning("AlignX", "Align X: unsupported edge type. Select a line, arc, or circle edge.")
                    Return
                End If
            End If

            refAngle = GetAlignXAngle(item)
            If Not Double.IsNaN(refAngle) Then Exit For
        Next

        If Double.IsNaN(refAngle) Then
            LogWarning("AlignX", "Align X: could not determine a reference angle from the selection.")
            Return
        End If

        ' If already at a cardinal angle (0°, 90°, 180°, 270°), step 90° CCW.
        Const CARDINAL_TOL As Double = 0.01   ' |sin(2θ)| < 0.01 ≈ within ~0.3° of a cardinal
        Dim rotAngle As Double
        If Math.Abs(Math.Sin(2.0 * refAngle)) < CARDINAL_TOL Then
            rotAngle = Math.PI / 2.0
        Else
            rotAngle = -refAngle
        End If

        RotateAllDocumentObjects(doc, 0.0, 0.0, 1.0, rotAngle)
        LogInfo("AlignX", $"Align X: rotated {rotAngle * 180.0 / Math.PI:F2}° around Z.")
        doc.Refresh()
    End Sub

    ' ── Ribbon helpers ────────────────────────────────────────────────────────

    ''' <summary>
    ''' Loads a .ico file by name from the same directory as the executing assembly.
    ''' Returns Nothing if the file does not exist or cannot be loaded.
    ''' </summary>
    Public Function LoadIcon(iconFileName As String) As System.Drawing.Icon
        Try
            Dim assemblyDir As String = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location)
            Dim iconPath As String = System.IO.Path.Combine(assemblyDir, "Icones", iconFileName)
            If System.IO.File.Exists(iconPath) Then
                Return New System.Drawing.Icon(iconPath)
            End If
        Catch
        End Try
        Return Nothing
    End Function

    ' ── EventWindow helpers ───────────────────────────────────────────────────

    Private Sub LogInfo(app As ESPRIT.Application, message As String)
        app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeInformation,
            MSG_SOURCE,
            message)
    End Sub

    Private Sub LogInfo(source As String, message As String)
        If _app Is Nothing Then Return
        _app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeInformation,
            source,
            message)
    End Sub

    Private Sub LogWarning(source As String, message As String)
        If _app Is Nothing Then Return
        _app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeWarning,
            source,
            message)
    End Sub

End Module
