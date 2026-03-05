Imports EspritGeometryBase
Imports EspritSolids

''' <summary>
''' Shared utility methods callable from any feature in the extension.
''' </summary>
Public Module ExtensionUtilities

    Private Const MSG_SOURCE As String = "AnalyzeFace"

    ' ── Public API ────────────────────────────────────────────────────────────

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
            Dim surface  As ISolidSurface  = face.SolidSurface
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
        Dim area     As Double = areaElem * (uMax - uMin) * (vMax - vMin)

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
        Dim duX   As Double = CDbl(evalU(1).X)
        Dim duY   As Double = CDbl(evalU(1).Y)
        Dim duZ   As Double = CDbl(evalU(1).Z)
        Dim radius As Double = Math.Sqrt(duX * duX + duY * duY + duZ * duZ)

        ' --- Axis direction: dP/dV at the parametric midpoint ---
        ' For a cylinder, dP/dV is constant and aligned with the revolution axis.
        Dim evalV As Object = surface.Evaluate(uMid, vMid, 0, 1)
        Dim dvX   As Double = CDbl(evalV(1).X)
        Dim dvY   As Double = CDbl(evalV(1).Y)
        Dim dvZ   As Double = CDbl(evalV(1).Z)
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
        Dim pBot As IComPoint  = surface.PointAlong(uMid, vMin)
        Dim botX As Double = pBot.X - radius * nBot.X
        Dim botY As Double = pBot.Y - radius * nBot.Y
        Dim botZ As Double = pBot.Z - radius * nBot.Z

        ' --- Top circle centre: surface point - R * outward normal at vMax ---
        Dim nTop As IComVector = surface.NormalAlong(uMid, vMax)
        Dim pTop As IComPoint  = surface.PointAlong(uMid, vMax)
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
                                        u       As Double,
                                        v       As Double) As Double
        Dim evalU As Object = surface.Evaluate(u, v, 1, 0)
        Dim duX   As Double = CDbl(evalU(1).X)
        Dim duY   As Double = CDbl(evalU(1).Y)
        Dim duZ   As Double = CDbl(evalU(1).Z)

        Dim evalV As Object = surface.Evaluate(u, v, 0, 1)
        Dim dvX   As Double = CDbl(evalV(1).X)
        Dim dvY   As Double = CDbl(evalV(1).Y)
        Dim dvZ   As Double = CDbl(evalV(1).Z)

        ' Cross product components
        Dim cpX As Double = duY * dvZ - duZ * dvY
        Dim cpY As Double = duZ * dvX - duX * dvZ
        Dim cpZ As Double = duX * dvY - duY * dvX

        Return Math.Sqrt(cpX * cpX + cpY * cpY + cpZ * cpZ)
    End Function

    ' ── Surface type name ─────────────────────────────────────────────────────

    Private Function GetSurfaceTypeName(typeCode As SolidSurfaceType) As String
        Select Case typeCode
            Case SolidSurfaceType.geoSurfaceUnknown  : Return "Unknown"
            Case SolidSurfaceType.geoSurfacePlane    : Return "Plane"
            Case SolidSurfaceType.geoSurfaceCylinder : Return "Cylinder"
            Case SolidSurfaceType.geoSurfaceCone     : Return "Cone"
            Case SolidSurfaceType.geoSurfaceSphere   : Return "Sphere"
            Case SolidSurfaceType.geoSurfaceTorus    : Return "Torus"
            Case SolidSurfaceType.geoSurfaceNurb     : Return "Nurb"
            Case SolidSurfaceType.geoSurfaceBlend    : Return "Blend"
            Case SolidSurfaceType.geoSurfaceSwept    : Return "Swept"
            Case SolidSurfaceType.geoSurfaceSpun     : Return "Spun"
            Case SolidSurfaceType.geoSurfaceOffset   : Return "Offset"
            Case Else                                 : Return $"Type({CInt(typeCode)})"
        End Select
    End Function

    ' ── EventWindow helper ────────────────────────────────────────────────────

    Private Sub LogInfo(app As ESPRIT.Application, message As String)
        app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeInformation,
            MSG_SOURCE,
            message)
    End Sub

End Module
