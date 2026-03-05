Imports ESPRIT.NetApi.Ribbon

''' <summary>
''' Feature: Add Parking Operation
''' Creates an EndMill tool (diameter 10) and adds a parking operation
''' named "parking" that sends X, Y, Z axes to Home position.
''' </summary>
Public Class AddParkingOperationFeature
    Implements IFeature

    Private Const RIBBON_GROUP_KEY As String = "AddParkingOperation_Group"
    Private Const RIBBON_BUTTON_KEY As String = "AddParkingOperation_Btn"
    Private Const PARKING_TOOL_ID As String = "EM-PARKING-10"
    Private Const PARKING_OP_NAME As String = "parking"

    Private ReadOnly _app As ESPRIT.Application

    Public Sub New(app As ESPRIT.Application)
        _app = app
    End Sub

    Public Sub Setup(tab As IRibbonTab) Implements IFeature.Setup
        Dim group As IRibbonGroup = tab.Groups.Add(RIBBON_GROUP_KEY, "Parking")

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

        group.Items.AddButton(RIBBON_BUTTON_KEY, "Add Parking Op", True, icon)
    End Sub

    Public Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean Implements IFeature.HandleButtonClick
        If e.Key = RIBBON_BUTTON_KEY Then
            e.Handled = True
            AddParkingOperation()
            Return True
        End If
        Return False
    End Function

    Public Sub Disconnect() Implements IFeature.Disconnect
        ' No per-feature cleanup needed; the tab is removed by Main.
    End Sub

    Private Sub AddParkingOperation()
        Dim doc As ESPRIT.Document = _app.Document

        If doc Is Nothing Then
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                "AddParkingOperation",
                "No document is open.")
            Return
        End If

        Try
            Dim techUtil As EspritTechnology.TechnologyUtility = doc.TechnologyUtility

            ' --- Step 1: Create EndMill tool (diameter 10) ---
            Dim endMill As EspritTechnology.Technology = techUtil.CreateTechnology(
                EspritConstants.espTechnologyType.espToolMillEndMill,
                doc.SystemUnit)
            endMill.Defaults(doc.SystemUnit)
            endMill.ToolID = PARKING_TOOL_ID
            endMill.ToolDiameter = 10.0

            Dim tools As EspritTools.Tools = doc.Tools
            tools.Add(endMill)

            ' --- Step 2: Create parking technology and set axes to Home ---
            Dim parkTech As EspritTechnology.TechMillPark = techUtil.CreateTechnology(
                EspritConstants.espTechnologyType.espTechMillPark,
                doc.SystemUnit)
            parkTech.OperationName = PARKING_OP_NAME
            parkTech.ToolChange = EspritConstants.espParkToolChange.espParkToolChangeTool
            parkTech.ToolID = "EM 10.0"
            parkTech.ParkAxisModeX = EspritConstants.espToolChangeMovementType.espToolChangeMovementHome
            parkTech.ParkAxisModeY = EspritConstants.espToolChangeMovementType.espToolChangeMovementHome
            parkTech.ParkAxisModeZ = EspritConstants.espToolChangeMovementType.espToolChangeMovementHome

            ' --- Step 3: Add parking operation to the document ---
            Dim partOp As ESPRIT.PartOperation = doc.PartOperations.Add(parkTech, Nothing, Nothing)
            partOp.Name = PARKING_OP_NAME

            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeInformation,
                "AddParkingOperation",
                $"Operation '{PARKING_OP_NAME}' added with tool '{PARKING_TOOL_ID}' (EndMill, diameter 10). Axes X/Y/Z set to Home.")

        Catch ex As Exception
            _app.EventWindow.AddMessage(
                EspritConstants.espMessageType.espMessageTypeWarning,
                "AddParkingOperation",
                $"Failed to add parking operation: {ex.Message}")
        End Try
    End Sub

End Class
