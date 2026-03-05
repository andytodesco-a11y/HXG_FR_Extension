Imports ESPRIT.NetApi.Ribbon
Imports EspritGeometryBase

''' <summary>
''' Feature: Set Collinear Axis Position
''' For each selected MachineOperation, checks whether the Z axis (tool side) can cover
''' the full toolpath Z range. If not, computes the optimal V axis position (workpiece side)
''' so that Z can reach all required positions, then applies it to CollinearAxesSolution.
'''
''' Kinematic convention assumed: Z_machine = WO_Z_machine + z_WO + V
''' (Z and V are co-directional — both contribute to closing the tool/workpiece gap).
''' If the machine uses the opposite convention, negate V_optimal in ProcessMachineOperation.
''' </summary>
Public Class SetCollinearAxisPositionFeature
    Implements IFeature

    Private Const RIBBON_GROUP_KEY As String = "SetCollinearAxisPos_Group"
    Private Const RIBBON_BUTTON_KEY As String = "SetCollinearAxisPos_Btn"
    Private Const AXIS_Z_NODENAME As String = "Z"
    Private Const AXIS_V_NODENAME As String = "V"
    Private Const SOURCE As String = "SetCollinearAxisPos"

    Private ReadOnly _app As ESPRIT.Application

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Dim group As IRibbonGroup = tab.Groups.Add(RIBBON_GROUP_KEY, "Collinear Axis")

        Dim icon As System.Drawing.Icon = Nothing
        Try
            Dim assemblyDir As String = System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location)
            Dim iconPath As String = System.IO.Path.Combine(assemblyDir, "HXG_Extension_France_Large.ico")
            If System.IO.File.Exists(iconPath) Then
                icon = New System.Drawing.Icon(iconPath)
            End If
        Catch
        End Try

        group.Items.AddButton(RIBBON_BUTTON_KEY, "Set Collinear Axis", True, icon)
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        If e.Key = RIBBON_BUTTON_KEY Then
            e.Handled = True
            SetCollinearAxisPositions()
            Return True
        End If
        Return False
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
    End Sub

    ' -------------------------------------------------------------------------

    Private Sub SetCollinearAxisPositions()
        Dim doc As ESPRIT.Document = _app.Document

        If doc Is Nothing Then
            LogWarning("No document is open.")
            Return
        End If

        ' doc.MachineOperations is not exposed in the .NET interop wrapper for ESPRIT.Document.
        ' A local Object variable is required to access it via COM dispatch.
        Dim docObj As Object = doc
        Dim machineOps As ESPRIT.MachineOperations = CType(docObj.MachineOperations, ESPRIT.MachineOperations)

        Dim processedCount As Integer = 0
        Dim alreadyOkCount As Integer = 0
        Dim errorCount As Integer = 0

        For i As Integer = 1 To machineOps.Count
            Dim machineOp As ESPRIT.MachineOperation = machineOps.Item(i)

            If Not machineOp.Selected Then Continue For

            Try
                Dim resultMsg As String = ProcessMachineOperation(machineOp)
                If resultMsg IsNot Nothing Then
                    LogInfo(resultMsg)
                    processedCount += 1
                Else
                    alreadyOkCount += 1
                End If
            Catch ex As Exception
                LogWarning($"[{machineOp.Name}] Error: {ex.Message}")
                errorCount += 1
            End Try
        Next

        If processedCount = 0 AndAlso alreadyOkCount = 0 AndAlso errorCount = 0 Then
            LogWarning("No selected MachineOperation found. Select operations in the operations list first.")
            Return
        End If

        Dim summary As String = $"Summary: {processedCount} operation(s) updated, {alreadyOkCount} already within Z range."
        If errorCount > 0 Then summary &= $" {errorCount} error(s)."
        LogInfo(summary)
    End Sub

    ''' <summary>
    ''' Analyses one MachineOperation and applies the optimal V position if needed.
    ''' Returns an info message when V is updated, Nothing when Z was already within range.
    ''' Throws on missing axes or unavailable data.
    ''' </summary>
    Private Function ProcessMachineOperation(machineOp As ESPRIT.MachineOperation) As String

        ' --- 1. Toolpath amplitude in Work Offset coordinates ---
        Dim box As IComBox = machineOp.GetBoundingBoxByWorkOffset()
        Dim zMinWO As Double = box.MinimumPoint.Z
        Dim zMaxWO As Double = box.MaximumPoint.Z

        ' --- 2. Work Offset origin in machine Z coordinates ---
        ' CumulativeTransformation gives the absolute machine-frame position of the WO origin,
        ' accounting for all parent fixture/workpiece transformations.
        Dim wo As ESPRIT.WorkOffset = machineOp.WorkOffset
        Dim transform As IComMatrix = wo.CumulativeTransformation
        Dim woZMachine As Double = transform.P.Z

        ' --- 3. Required Z range in machine coordinates (V = 0) ---
        ' With V at its neutral position: Z_machine = WO_Z_machine + z_WO
        Dim zMachineMin As Double = woZMachine + zMinWO
        Dim zMachineMax As Double = woZMachine + zMaxWO

        ' --- 4. Retrieve Z and V axis travel limits from the machine setup ---
        Dim machineSetup As ESPRIT.MachineSetup = machineOp.PartOperation.GetMachineSetupForOperation(True)
        Dim axisInfos As ESPRIT.MachineAxisDataInfos = machineSetup.MachineAxisDataInfos

        Dim zLower As Double = Double.NegativeInfinity
        Dim zUpper As Double = Double.PositiveInfinity
        Dim vLower As Double = Double.NegativeInfinity
        Dim vUpper As Double = Double.PositiveInfinity
        Dim zFound As Boolean = False
        Dim vFound As Boolean = False

        For j As Integer = 1 To axisInfos.Count
            Dim info As ESPRIT.MachineAxisDataInfo = axisInfos.Item(j)
            Dim nodeName As String = info.FullNodeName

            If IsAxisNode(nodeName, AXIS_Z_NODENAME) Then
                zLower = info.LowerLimit
                zUpper = info.UpperLimit
                zFound = True
            ElseIf IsAxisNode(nodeName, AXIS_V_NODENAME) Then
                vLower = info.LowerLimit
                vUpper = info.UpperLimit
                vFound = True
            End If
        Next

        If Not zFound Then
            Throw New InvalidOperationException(
                $"Z axis node '{AXIS_Z_NODENAME}' not found in MachineAxisDataInfos.")
        End If

        ' --- 5. Check if Z already covers the full toolpath range (V = 0) ---
        If zMachineMin >= zLower AndAlso zMachineMax <= zUpper Then
            ' Z can reach all positions without moving V — leave solution as-is.
            Return Nothing
        End If

        ' --- 6. Compute optimal V to centre the toolpath inside the Z travel range ---
        ' With V applied: Z_machine_needed = WO_Z + z_WO + V
        ' Centre condition: (zMachineMin + V + zMachineMax + V) / 2 = (zLower + zUpper) / 2
        '   => V_optimal = (zLower + zUpper) / 2 - (zMachineMin + zMachineMax) / 2
        Dim vOptimal As Double = (zLower + zUpper) / 2.0 - (zMachineMin + zMachineMax) / 2.0

        ' --- 7. Clamp V to its own travel limits ---
        If vFound Then
            vOptimal = Math.Max(vLower, Math.Min(vUpper, vOptimal))
        End If

        ' --- 8. Verify the resulting Z range is reachable after applying V_optimal ---
        Dim zEffectiveMin As Double = zMachineMin + vOptimal
        Dim zEffectiveMax As Double = zMachineMax + vOptimal
        Dim fullyReachable As Boolean = zEffectiveMin >= zLower AndAlso zEffectiveMax <= zUpper

        ' --- 9. Apply to CollinearAxesSolution ---
        Dim solution As ESPRIT.CollinearAxesSolution = machineOp.CollinearAxesSolution

        Dim vAxis As ESPRIT.CollinearAxis = Nothing
        For k As Integer = 1 To solution.Count
            Dim axis As ESPRIT.CollinearAxis = solution.Item(k)
            If axis.Name = AXIS_V_NODENAME Then
                vAxis = axis
                Exit For
            End If
        Next

        If vAxis Is Nothing Then
            Throw New InvalidOperationException(
                $"V axis '{AXIS_V_NODENAME}' not found in CollinearAxesSolution.")
        End If

        vAxis.MovementMode = EspritConstants.espCollinearMovement.espCollinearMovementPosition
        vAxis.InputMode = EspritConstants.espCollinearInputMode.espCollinearInputModeMachine
        vAxis.Value = vOptimal

        ' --- 10. Build result message ---
        Dim msg As String =
            $"[{machineOp.Name}] V = {vOptimal:F3} mm" &
            $" | Z toolpath (machine, after V): [{zEffectiveMin:F3} ; {zEffectiveMax:F3}]" &
            $" | Z limits: [{zLower:F3} ; {zUpper:F3}]"

        If Not fullyReachable Then
            msg &= " -- WARNING: Z cannot fully cover the range even with V at its limits."
        End If

        Return msg
    End Function

    ''' <summary>
    ''' Returns True when fullNodeName matches the target axis name exactly
    ''' or ends with it after a path separator (e.g. "TC/Z" matches "Z").
    ''' </summary>
    Private Shared Function IsAxisNode(fullNodeName As String, axisName As String) As Boolean
        Return fullNodeName = axisName OrElse
               fullNodeName.EndsWith("/" & axisName) OrElse
               fullNodeName.EndsWith("\" & axisName) OrElse
               fullNodeName.EndsWith(":" & axisName)
    End Function

    Private Sub LogInfo(message As String)
        _app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeInformation, SOURCE, message)
    End Sub

    Private Sub LogWarning(message As String)
        _app.EventWindow.AddMessage(
            EspritConstants.espMessageType.espMessageTypeWarning, SOURCE, message)
    End Sub

End Class
