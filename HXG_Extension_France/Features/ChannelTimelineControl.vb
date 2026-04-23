Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' Double-buffered Panel — eliminates flicker during GDI+ rendering.
''' </summary>
Friend NotInheritable Class DoubleBufferedPanel
    Inherits Panel
    Public Sub New()
        DoubleBuffered = True
        SetStyle(ControlStyles.AllPaintingInWmPaint Or
                 ControlStyles.UserPaint Or
                 ControlStyles.OptimizedDoubleBuffer, True)
    End Sub
End Class

''' <summary>
''' Gantt-style timeline UserControl for multi-turret channel analysis.
''' Hosted inside a dockable ESPRIT IPane (see ChannelTimelineFeature).
'''
''' Layout (top → bottom):
'''   Toolbar  : Refresh button + cycle time + channel count info
'''   Timeline : Scrollable GDI+ panel — one row per channel, bars per item
'''   Legend   : Color swatch for each item type
'''
''' Data source: Document.Program → IChannels → IChannelItems + ISyncs
''' </summary>
Public Class ChannelTimelineControl
    Inherits UserControl

    ' ── Layout ────────────────────────────────────────────────────────────────
    Private Const LABEL_WIDTH As Integer = 130
    Private Const ROW_HEIGHT As Integer = 58
    Private Const HEADER_HEIGHT As Integer = 30
    Private Const LEFT_PADDING As Integer = 8
    Private Const RIGHT_PADDING As Integer = 20
    Private Const BOTTOM_PADDING As Integer = 14
    Private Const MIN_ITEM_PIXELS As Integer = 2
    Private Const RULER_TICKS As Integer = 10
    Private Const MIN_DRAW_WIDTH As Integer = 750   ' minimum bar area width (px)

    ' ── Item type bit-flags (espChannelItemType) ──────────────────────────────
    Private Const TYPE_TOOL_CHANGE As Integer = 1
    Private Const TYPE_MACHINE_OP As Integer = 2
    Private Const TYPE_LINK As Integer = 4
    Private Const TYPE_SYNC_NODE As Integer = 8
    Private Const TYPE_TOOLPATH As Integer = 16
    Private Const TYPE_SETUP_CHANGE As Integer = 32

    ' ── Technology categories (espTechnologyCategory) ─────────────────────────
    Private Const TECH_CAT_MILLING As Integer = 4
    Private Const TECH_CAT_TURNING As Integer = 5

    ' ── Colors ────────────────────────────────────────────────────────────────
    Private Shared ReadOnly CLR_MACHINE_OP_TURNING As Color = Color.FromArgb(65, 130, 210)
    Private Shared ReadOnly CLR_MACHINE_OP_MILLING As Color = Color.FromArgb(235, 140, 40)
    Private Shared ReadOnly CLR_MACHINE_OP_OTHER As Color = Color.FromArgb(95, 170, 95)
    Private Shared ReadOnly CLR_TOOLPATH As Color = Color.FromArgb(65, 170, 85)
    Private Shared ReadOnly CLR_TOOL_CHANGE As Color = Color.FromArgb(240, 200, 40)
    Private Shared ReadOnly CLR_SETUP_CHANGE As Color = Color.FromArgb(145, 85, 185)
    Private Shared ReadOnly CLR_LINK As Color = Color.FromArgb(185, 185, 195)
    Private Shared ReadOnly CLR_SYNC_NODE As Color = Color.FromArgb(210, 55, 55)
    Private Shared ReadOnly CLR_UNKNOWN As Color = Color.FromArgb(200, 200, 200)
    Private Shared ReadOnly CLR_SYNC_LINE As Color = Color.FromArgb(210, 55, 55)

    ' ── Controls ──────────────────────────────────────────────────────────────
    Private _toolPanel As Panel
    Private _refreshButton As Button
    Private _infoLabel As Label
    Private _zoomLabel As Label
    Private _zoomResetButton As Button
    Private _scrollPanel As Panel
    Private _timelinePanel As DoubleBufferedPanel
    Private _legendPanel As Panel
    Private _tooltip As ToolTip

    ' ── State ─────────────────────────────────────────────────────────────────
    Private ReadOnly _app As ESPRIT.Application
    Private _channels As New List(Of ChannelData)
    Private _syncs As New List(Of SyncData)
    Private _channelIndexToRow As New Dictionary(Of Integer, Integer)
    Private _totalTime As Double
    Private _lastTooltipItem As ItemData
    Private _zoomX As Double = 1.0
    Private _baseInfoText As String = ""

    ' ── Constructor ───────────────────────────────────────────────────────────

    Public Sub New(app As ESPRIT.Application)
        _app = app
        InitializeComponent()
        LoadData()
    End Sub

    ' ── UI Setup ─────────────────────────────────────────────────────────────

    Private Sub InitializeComponent()
        SuspendLayout()

        MinimumSize = New Size(640, 320)
        BackColor = Color.White

        ' ── Toolbar ──────────────────────────────────────────────────────────
        _toolPanel = New Panel() With {
            .Dock = DockStyle.Top,
            .Height = 38,
            .BackColor = Color.FromArgb(240, 240, 246)
        }

        _refreshButton = New Button() With {
            .Text = Strings.Timeline_RefreshButton,
            .Width = 92,
            .Height = 28,
            .Location = New Point(6, 5),
            .FlatStyle = FlatStyle.System
        }
        AddHandler _refreshButton.Click, AddressOf OnRefreshClick

        _infoLabel = New Label() With {
            .AutoSize = True,
            .Location = New Point(106, 9),
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = Color.FromArgb(55, 55, 70)
        }
        AddHandler _infoLabel.TextChanged, AddressOf OnInfoLabelTextChanged

        ' Zoom readout + reset button — laid out inline right after the info label.
        _zoomLabel = New Label() With {
            .AutoSize = True,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI", 8.5F),
            .ForeColor = Color.FromArgb(55, 55, 70)
        }

        _zoomResetButton = New Button() With {
            .Text = "100%",
            .Width = 60,
            .Height = 24,
            .FlatStyle = FlatStyle.System
        }
        AddHandler _zoomResetButton.Click, AddressOf OnZoomResetClick

        _toolPanel.Controls.Add(_refreshButton)
        _toolPanel.Controls.Add(_infoLabel)
        _toolPanel.Controls.Add(_zoomLabel)
        _toolPanel.Controls.Add(_zoomResetButton)

        UpdateZoomReadout()

        ' ── Legend ───────────────────────────────────────────────────────────
        _legendPanel = New Panel() With {
            .Dock = DockStyle.Bottom,
            .Height = 36,
            .BackColor = Color.FromArgb(245, 245, 250)
        }
        AddHandler _legendPanel.Paint, AddressOf OnLegendPaint

        ' ── Scroll panel ─────────────────────────────────────────────────────
        _scrollPanel = New Panel() With {
            .Dock = DockStyle.Fill,
            .AutoScroll = True,
            .BackColor = Color.White
        }

        ' ── Timeline panel ───────────────────────────────────────────────────
        _timelinePanel = New DoubleBufferedPanel() With {
            .Location = New Point(0, 0),
            .BackColor = Color.White
        }
        AddHandler _timelinePanel.Paint, AddressOf OnTimelinePaint
        AddHandler _timelinePanel.MouseMove, AddressOf OnTimelineMouseMove
        AddHandler _timelinePanel.MouseLeave, AddressOf OnTimelineMouseLeave
        AddHandler _timelinePanel.MouseWheel, AddressOf OnTimelineMouseWheel

        _scrollPanel.Controls.Add(_timelinePanel)

        ' ── Tooltip ──────────────────────────────────────────────────────────
        _tooltip = New ToolTip() With {
            .InitialDelay = 300,
            .ShowAlways = True
        }

        ' Add in reverse DockStyle order
        Controls.Add(_scrollPanel)
        Controls.Add(_legendPanel)
        Controls.Add(_toolPanel)

        AddHandler Resize, AddressOf OnControlResize

        ResumeLayout(False)
    End Sub

    ' ── Public API ───────────────────────────────────────────────────────────

    Public Sub RefreshData()
        LoadData()
    End Sub

    ' ── Data loading ─────────────────────────────────────────────────────────

    Private Sub LoadData()
        _channels.Clear()
        _syncs.Clear()
        _channelIndexToRow.Clear()
        _totalTime = 0
        _lastTooltipItem = Nothing

        Try
            Dim doc As ESPRIT.Document = _app.Document
            If doc Is Nothing Then
                _baseInfoText = Strings.Timeline_NoProgram
                _infoLabel.Text = _baseInfoText
                FinalizeLoad()
                Return
            End If

            ' Let any exception propagate to the outer Catch so the real error is visible.
            Dim prog As Object = doc.Program

            If prog Is Nothing Then
                _baseInfoText = Strings.Timeline_NoProgram
                _infoLabel.Text = _baseInfoText
                FinalizeLoad()
                Return
            End If

            _totalTime = CDbl(prog.TotalCycleTime)

            ' Time lookup: encoded key (channelIndex * OFFSET + itemIndex) → cumulative start time
            Const OFFSET As Long = 1000000L
            Dim timeLookup As New Dictionary(Of Long, Double)

            ' MachineOperation lookup: same key → op name + technology category.
            ' IChannelItem has no direct link to IMachineOperation, so we invert via
            ' op.Before.ChannelItem / op.Before.Channel. Category is resolved through
            ' TechnologyUtility.GetCategory(PartOperation.Technology.TechnologyType) —
            ' strongly typed so the COM vtable call lands on the right GetCategory overload.
            Dim opNameLookup As New Dictionary(Of Long, String)
            Dim opCategoryLookup As New Dictionary(Of Long, Integer)
            Try
                Dim techUtil As EspritTechnology.TechnologyUtility = Nothing
                Try
                    techUtil = CType(doc.TechnologyUtility, EspritTechnology.TechnologyUtility)
                Catch
                End Try
                Dim opsColl = doc.MachineOperations
                Dim opsCount As Integer = CInt(opsColl.Count)
                For oi As Integer = 1 To opsCount
                    Try
                        Dim op = opsColl.Item(oi)
                        Dim pos = op.Before
                        If pos IsNot Nothing AndAlso pos.ChannelItem IsNot Nothing AndAlso pos.Channel IsNot Nothing Then
                            Dim chIdx As Integer = CInt(pos.Channel.Index)
                            Dim ciIdx As Long = CLng(pos.ChannelItem.Index)
                            Dim key As Long = CLng(chIdx) * OFFSET + ciIdx
                            opNameLookup(key) = CStr(op.Name)
                            If techUtil IsNot Nothing Then
                                Try
                                    Dim partOp = op.PartOperation
                                    If partOp IsNot Nothing Then
                                        Dim tech As EspritTechnology.ITechnology = CType(partOp.Technology, EspritTechnology.ITechnology)
                                        If tech IsNot Nothing Then
                                            Dim cat As EspritConstants.espTechnologyCategory = techUtil.GetCategory(tech.TechnologyType)
                                            opCategoryLookup(key) = CInt(cat)
                                        End If
                                    End If
                                Catch
                                End Try
                            End If
                        End If
                    Catch
                    End Try
                Next
            Catch
            End Try

            Dim channelsColl = prog.Channels
            Dim channelCount As Integer = CInt(channelsColl.Count)

            For ci As Integer = 1 To channelCount
                Dim ch = channelsColl.Item(ci)
                Dim apiChannelIndex As Integer = CInt(ch.Index)
                Dim cd As New ChannelData() With {
                    .Name = CStr(ch.Name),
                    .Index = apiChannelIndex
                }
                ' Map the real API channel index to its 0-based row in _channels
                _channelIndexToRow(apiChannelIndex) = _channels.Count

                Dim itemsColl = ch.ChannelItems
                Dim itemCount As Integer = CInt(itemsColl.Count)
                Dim runningTime As Double = 0

                For ii As Integer = 1 To itemCount
                    Dim apiItem = itemsColl.Item(ii)
                    ' Cast to IChannelItem to access .Type via the correct COM interface.
                    ' Late binding uses IDispatch of the concrete type (e.g. IMachineOperation)
                    ' which does not expose Type; the IChannelItem interface does.
                    Dim channelItem As ESPRIT.IChannelItem = CType(apiItem, ESPRIT.IChannelItem)
                    Dim id As New ItemData() With {
                        .ItemType = CInt(channelItem.Type),
                        .CycleTime = CDbl(channelItem.CycleTime),
                        .StartTime = runningTime,
                        .ItemIndex = CLng(channelItem.Index)
                    }
                    If id.ItemType = TYPE_MACHINE_OP Then
                        Dim opKey As Long = CLng(apiChannelIndex) * OFFSET + id.ItemIndex
                        Dim opName As String = Nothing
                        If opNameLookup.TryGetValue(opKey, opName) Then
                            id.OperationName = opName
                        End If
                        Dim opCat As Integer
                        If opCategoryLookup.TryGetValue(opKey, opCat) Then
                            id.OperationCategory = opCat
                        End If
                    End If
                    timeLookup(CLng(apiChannelIndex) * OFFSET + id.ItemIndex) = runningTime
                    runningTime += id.CycleTime
                    cd.Items.Add(id)
                Next

                _channels.Add(cd)
            Next

            ' Sync connections
            Dim syncsColl = prog.Syncs
            Dim syncCount As Integer = CInt(syncsColl.Count)

            For si As Integer = 1 To syncCount
                Dim apiSync = syncsColl.Item(si)
                Dim sd As New SyncData() With {.SyncId = CInt(apiSync.Id)}

                Dim posColl = apiSync.ChannelItemPositions
                Dim posCount As Integer = CInt(posColl.Count)

                For pi As Integer = 1 To posCount
                    Try
                        Dim pos = posColl.Item(pi)
                        Dim spd As New SyncPositionData() With {
                            .ChannelIndex = CInt(pos.Channel.Index),
                            .ItemIndex = CLng(pos.ChannelItem.Index)
                        }
                        Dim key As Long = CLng(spd.ChannelIndex) * OFFSET + spd.ItemIndex
                        If timeLookup.ContainsKey(key) Then
                            spd.SyncTime = timeLookup(key)
                        End If
                        sd.Positions.Add(spd)
                    Catch
                    End Try
                Next

                If sd.Positions.Count >= 2 Then
                    _syncs.Add(sd)
                End If
            Next

            _baseInfoText = String.Format(Strings.Timeline_TotalTime, _totalTime) &
                            "    |    " &
                            String.Format(Strings.Timeline_Channels, channelCount)
            _infoLabel.Text = _baseInfoText

        Catch ex As Exception
            _baseInfoText = $"[{ex.GetType().Name}] {ex.Message}"
            _infoLabel.Text = _baseInfoText
        End Try

        FinalizeLoad()
    End Sub

    Private Sub FinalizeLoad()
        UpdatePanelSize()
        _timelinePanel.Invalidate()
    End Sub

    Private Function GetDrawWidth() As Integer
        Dim baseWidth As Integer = Math.Max(_scrollPanel.ClientSize.Width - LABEL_WIDTH - RIGHT_PADDING, MIN_DRAW_WIDTH)
        Return CInt(baseWidth * _zoomX)
    End Function

    Private Sub UpdatePanelSize()
        Dim rowCount As Integer = Math.Max(_channels.Count, 1)
        Dim h As Integer = HEADER_HEIGHT + rowCount * ROW_HEIGHT + BOTTOM_PADDING
        Dim w As Integer = LABEL_WIDTH + GetDrawWidth() + RIGHT_PADDING
        _timelinePanel.Size = New Size(w, h)
    End Sub

    ' ── Painting ─────────────────────────────────────────────────────────────

    Private Sub OnTimelinePaint(sender As Object, e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

        If _channels.Count = 0 Then
            DrawNoDataMessage(g)
            Return
        End If

        Dim drawWidth As Integer = GetDrawWidth()

        DrawRuler(g, drawWidth)

        For i As Integer = 0 To _channels.Count - 1
            DrawChannelRow(g, _channels(i), i, drawWidth)
        Next

        ' Sync connections drawn last so they appear on top
        DrawSyncs(g, drawWidth)
    End Sub

    Private Sub DrawNoDataMessage(g As Graphics)
        Using f As New Font("Segoe UI", 10.0F)
            Dim msg As String = Strings.Timeline_NoProgram
            Dim sz As SizeF = g.MeasureString(msg, f)
            g.DrawString(msg, f, Brushes.Gray,
                (_timelinePanel.Width - sz.Width) / 2.0F,
                (_timelinePanel.Height - sz.Height) / 2.0F)
        End Using
    End Sub

    ' ── Ruler ────────────────────────────────────────────────────────────────

    Private Sub DrawRuler(g As Graphics, drawWidth As Integer)
        ' Background
        Using br As New SolidBrush(Color.FromArgb(236, 236, 246))
            g.FillRectangle(br, 0, 0, _timelinePanel.Width, HEADER_HEIGHT)
        End Using
        ' Bottom border
        Using p As New Pen(Color.FromArgb(195, 195, 215))
            g.DrawLine(p, 0, HEADER_HEIGHT - 1, _timelinePanel.Width, HEADER_HEIGHT - 1)
        End Using
        ' Label column divider
        Using p As New Pen(Color.FromArgb(195, 195, 215))
            g.DrawLine(p, LABEL_WIDTH - 1, 0, LABEL_WIDTH - 1, HEADER_HEIGHT)
        End Using

        If _totalTime <= 0 Then Return

        Using f As New Font("Segoe UI", 7.5F)
            For tick As Integer = 0 To RULER_TICKS
                Dim ratio As Double = tick / CDbl(RULER_TICKS)
                Dim x As Integer = LABEL_WIDTH + CInt(ratio * drawWidth)
                Dim label As String = $"{ratio * _totalTime:F1}s"
                Dim sz As SizeF = g.MeasureString(label, f)

                Dim labelX As Single = x - sz.Width / 2.0F
                labelX = Math.Max(LABEL_WIDTH, Math.Min(labelX, LABEL_WIDTH + drawWidth - sz.Width))
                g.DrawString(label, f, Brushes.DimGray, labelX, 3.0F)

                Using tickPen As New Pen(Color.FromArgb(165, 165, 195))
                    g.DrawLine(tickPen, x, HEADER_HEIGHT - 9, x, HEADER_HEIGHT - 1)
                End Using
            Next
        End Using
    End Sub

    ' ── Channel rows ─────────────────────────────────────────────────────────

    Private Sub DrawChannelRow(g As Graphics, cd As ChannelData, rowIndex As Integer, drawWidth As Integer)
        Dim rowY As Integer = HEADER_HEIGHT + rowIndex * ROW_HEIGHT
        Dim barY As Integer = rowY + 9
        Dim barH As Integer = ROW_HEIGHT - 18

        ' Alternating row background
        Dim bgColor As Color = If(rowIndex Mod 2 = 0,
                                  Color.FromArgb(253, 253, 255),
                                  Color.FromArgb(246, 246, 254))
        Using br As New SolidBrush(bgColor)
            g.FillRectangle(br, 0, rowY, _timelinePanel.Width, ROW_HEIGHT)
        End Using

        ' Row separator
        Using p As New Pen(Color.FromArgb(212, 212, 224))
            g.DrawLine(p, 0, rowY + ROW_HEIGHT - 1, _timelinePanel.Width, rowY + ROW_HEIGHT - 1)
        End Using

        ' Label column divider
        Using p As New Pen(Color.FromArgb(195, 195, 215))
            g.DrawLine(p, LABEL_WIDTH - 1, rowY, LABEL_WIDTH - 1, rowY + ROW_HEIGHT)
        End Using

        ' Channel name (right-aligned in label column)
        Using f As New Font("Segoe UI", 8.5F, FontStyle.Bold)
            Dim labelRect As New RectangleF(LEFT_PADDING, rowY, LABEL_WIDTH - LEFT_PADDING - 6, ROW_HEIGHT)
            Dim sf As New StringFormat() With {
                .Alignment = StringAlignment.Far,
                .LineAlignment = StringAlignment.Center,
                .Trimming = StringTrimming.EllipsisCharacter
            }
            g.DrawString(cd.Name, f, Brushes.DarkSlateGray, labelRect, sf)
        End Using

        If _totalTime <= 0 OrElse cd.Items.Count = 0 Then
            Using f As New Font("Segoe UI", 8.0F, FontStyle.Italic)
                g.DrawString(Strings.Timeline_ChannelEmpty, f, Brushes.LightSlateGray,
                             LABEL_WIDTH + 8.0F, rowY + (ROW_HEIGHT - 14) / 2.0F)
            End Using
            Return
        End If

        For Each id As ItemData In cd.Items
            DrawItem(g, id, barY, barH, drawWidth)
        Next
    End Sub

    Private Sub DrawItem(g As Graphics, id As ItemData, barY As Integer, barH As Integer, drawWidth As Integer)
        Dim x As Integer = LABEL_WIDTH + CInt((id.StartTime / _totalTime) * drawWidth)

        ' SyncNode → thin dashed vertical marker (zero width)
        If (id.ItemType And TYPE_SYNC_NODE) <> 0 Then
            Using p As New Pen(CLR_SYNC_NODE, 1.5F) With {.DashStyle = DashStyle.Dash}
                g.DrawLine(p, x, barY, x, barY + barH)
            End Using
            Return
        End If

        If id.CycleTime <= 0 Then Return

        Dim w As Integer = Math.Max(MIN_ITEM_PIXELS, CInt((id.CycleTime / _totalTime) * drawWidth))
        Dim fillColor As Color = GetItemColor(id)
        Dim rect As New Rectangle(x, barY, w, barH)

        Using br As New SolidBrush(fillColor)
            g.FillRectangle(br, rect)
        End Using
        Using p As New Pen(DarkenColor(fillColor, 0.25F), 1)
            g.DrawRectangle(p, rect)
        End Using

        ' Time label — only if bar is wide enough to be legible
        If w > 30 Then
            Using f As New Font("Segoe UI", 7.0F)
                Dim sf As New StringFormat() With {
                    .Alignment = StringAlignment.Center,
                    .LineAlignment = StringAlignment.Center,
                    .Trimming = StringTrimming.EllipsisCharacter
                }
                Dim textColor As Color = If(IsLightColor(fillColor), Color.FromArgb(40, 40, 55), Color.White)
                Using tbr As New SolidBrush(textColor)
                    g.DrawString($"{id.CycleTime:F1}s", f, tbr,
                                 New RectangleF(x, barY, w, barH), sf)
                End Using
            End Using
        End If
    End Sub

    ' ── Sync connections ──────────────────────────────────────────────────────

    Private Sub DrawSyncs(g As Graphics, drawWidth As Integer)
        If _totalTime <= 0 OrElse _syncs.Count = 0 Then Return

        For Each sd As SyncData In _syncs
            If sd.Positions.Count < 2 Then Continue For

            ' All positions of a sync share one X coordinate — the latest arrival time.
            ' This ensures the vertical sync line is perfectly aligned across all channels.
            Dim latestTime As Double = 0
            For Each pos As SyncPositionData In sd.Positions
                If pos.SyncTime > latestTime Then latestTime = pos.SyncTime
            Next
            Dim syncX As Integer = LABEL_WIDTH + CInt((latestTime / _totalTime) * drawWidth)

            ' Resolve participating rows via the API-index map (robust to non-sequential numbering)
            Dim rows As New List(Of Integer)
            For Each pos As SyncPositionData In sd.Positions
                Dim r As Integer
                If _channelIndexToRow.TryGetValue(pos.ChannelIndex, r) Then
                    If Not rows.Contains(r) Then rows.Add(r)
                End If
            Next
            If rows.Count = 0 Then Continue For
            rows.Sort()
            Dim minRow As Integer = rows(0)
            Dim maxRow As Integer = rows(rows.Count - 1)

            ' Dotted marker spanning each participating row
            Using dotPen As New Pen(Color.FromArgb(190, CLR_SYNC_LINE.R, CLR_SYNC_LINE.G, CLR_SYNC_LINE.B), 1.5F) With {.DashStyle = DashStyle.Dot}
                For Each r As Integer In rows
                    Dim rTop As Integer = HEADER_HEIGHT + r * ROW_HEIGHT + 4
                    Dim rBot As Integer = HEADER_HEIGHT + r * ROW_HEIGHT + ROW_HEIGHT - 4
                    g.DrawLine(dotPen, syncX, rTop, syncX, rBot)
                Next
            End Using

            ' Straight vertical connector between the bottom of the top row and the top of the bottom row
            Dim connTop As Integer = HEADER_HEIGHT + minRow * ROW_HEIGHT + ROW_HEIGHT - 4
            Dim connBot As Integer = HEADER_HEIGHT + maxRow * ROW_HEIGHT + 4
            If connTop < connBot Then
                Using connPen As New Pen(Color.FromArgb(150, CLR_SYNC_LINE.R, CLR_SYNC_LINE.G, CLR_SYNC_LINE.B), 1.5F) With {.DashStyle = DashStyle.Dash}
                    g.DrawLine(connPen, syncX, connTop, syncX, connBot)
                End Using
            End If

            ' Diamond at the midpoint of each participating row
            For Each r As Integer In rows
                Dim rMid As Integer = HEADER_HEIGHT + r * ROW_HEIGHT + ROW_HEIGHT \ 2
                DrawDiamond(g, syncX, rMid, 5, CLR_SYNC_LINE)
            Next

            ' Sync ID label just to the right of the line, at the top row
            Dim labelX As Single = syncX + 4.0F
            Dim labelY As Single = HEADER_HEIGHT + minRow * ROW_HEIGHT + 6.0F
            Using f As New Font("Segoe UI", 7.0F, FontStyle.Bold)
                Dim label As String = $"S{sd.SyncId}"
                Dim sz As SizeF = g.MeasureString(label, f)
                Using bgBr As New SolidBrush(Color.FromArgb(210, 255, 255, 255))
                    g.FillRectangle(bgBr, labelX - 1.0F, labelY - 1.0F, sz.Width + 2.0F, sz.Height + 2.0F)
                End Using
                Using lbr As New SolidBrush(CLR_SYNC_LINE)
                    g.DrawString(label, f, lbr, labelX, labelY)
                End Using
            End Using
        Next
    End Sub

    Private Sub DrawDiamond(g As Graphics, cx As Integer, cy As Integer, size As Integer, color As Color)
        Dim pts As Point() = {
            New Point(cx, cy - size),
            New Point(cx + size, cy),
            New Point(cx, cy + size),
            New Point(cx - size, cy)
        }
        Using br As New SolidBrush(color)
            g.FillPolygon(br, pts)
        End Using
    End Sub

    ' ── Legend ────────────────────────────────────────────────────────────────

    Private Sub OnLegendPaint(sender As Object, e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        Using f As New Font("Segoe UI", 8.0F)
            Const SW As Integer = 13
            Const GAP As Integer = 4
            Const SPACING As Integer = 10
            Dim x As Integer = 10
            Dim midY As Integer = _legendPanel.Height \ 2

            x = DrawLegendEntry(g, f, CLR_MACHINE_OP_TURNING, Strings.Timeline_Legend_MachineOp_Turning, x, midY, SW, GAP) + SPACING
            x = DrawLegendEntry(g, f, CLR_MACHINE_OP_MILLING, Strings.Timeline_Legend_MachineOp_Milling, x, midY, SW, GAP) + SPACING
            x = DrawLegendEntry(g, f, CLR_MACHINE_OP_OTHER, Strings.Timeline_Legend_MachineOp_Other, x, midY, SW, GAP) + SPACING
            x = DrawLegendEntry(g, f, CLR_TOOL_CHANGE, Strings.Timeline_Legend_ToolChange, x, midY, SW, GAP) + SPACING
            x = DrawLegendEntry(g, f, CLR_SETUP_CHANGE, Strings.Timeline_Legend_SetupChange, x, midY, SW, GAP) + SPACING
            x = DrawLegendEntry(g, f, CLR_LINK, Strings.Timeline_Legend_Link, x, midY, SW, GAP) + SPACING
            DrawLegendEntry(g, f, CLR_SYNC_LINE, Strings.Timeline_Legend_Sync, x, midY, SW, GAP)
        End Using
    End Sub

    ''' <summary>Draws one legend entry; returns the right edge of the drawn content.</summary>
    Private Function DrawLegendEntry(g As Graphics, f As Font, color As Color, label As String,
                                      startX As Integer, midY As Integer,
                                      swatchSize As Integer, gap As Integer) As Integer
        Dim swatchY As Integer = midY - swatchSize \ 2
        Using br As New SolidBrush(color)
            g.FillRectangle(br, startX, swatchY, swatchSize, swatchSize)
        End Using
        Using p As New Pen(DarkenColor(color, 0.25F))
            g.DrawRectangle(p, startX, swatchY, swatchSize, swatchSize)
        End Using
        Dim textX As Integer = startX + swatchSize + gap
        g.DrawString(label, f, Brushes.DarkSlateGray, textX, midY - 7.0F)
        Return textX + CInt(g.MeasureString(label, f).Width)
    End Function

    ' ── Mouse / Tooltip ───────────────────────────────────────────────────────

    Private Sub OnTimelineMouseMove(sender As Object, e As MouseEventArgs)
        Dim item As ItemData = HitTestItem(e.X, e.Y)
        If item Is _lastTooltipItem Then Return
        _lastTooltipItem = item
        If item IsNot Nothing Then
            Dim tip As String = $"{GetItemTypeName(item)}  —  {item.CycleTime:F3} s"
            If Not String.IsNullOrEmpty(item.OperationName) Then
                tip = $"{item.OperationName}" & Environment.NewLine & tip
            End If
            _tooltip.SetToolTip(_timelinePanel, tip)
        Else
            _tooltip.SetToolTip(_timelinePanel, "")
        End If
    End Sub

    Private Sub OnTimelineMouseLeave(sender As Object, e As EventArgs)
        _lastTooltipItem = Nothing
        _tooltip.SetToolTip(_timelinePanel, "")
    End Sub

    Private Function HitTestItem(mx As Integer, my As Integer) As ItemData
        If _totalTime <= 0 OrElse _channels.Count = 0 Then Return Nothing

        Dim drawWidth As Integer = GetDrawWidth()
        Dim relY As Integer = my - HEADER_HEIGHT
        If relY < 0 Then Return Nothing

        Dim rowIndex As Integer = relY \ ROW_HEIGHT
        If rowIndex < 0 OrElse rowIndex >= _channels.Count Then Return Nothing

        Dim relX As Integer = mx - LABEL_WIDTH
        If relX < 0 OrElse relX > drawWidth Then Return Nothing

        Dim hitTime As Double = (relX / CDbl(drawWidth)) * _totalTime

        For Each id As ItemData In _channels(rowIndex).Items
            Dim endTime As Double = id.StartTime + Math.Max(id.CycleTime, 0.001)
            If hitTime >= id.StartTime AndAlso hitTime < endTime Then
                Return id
            End If
        Next
        Return Nothing
    End Function

    ' ── Color helpers ─────────────────────────────────────────────────────────

    Private Function GetItemColor(id As ItemData) As Color
        Dim t As Integer = id.ItemType
        If (t And TYPE_SYNC_NODE) <> 0 Then Return CLR_SYNC_NODE
        If (t And TYPE_TOOL_CHANGE) <> 0 Then Return CLR_TOOL_CHANGE
        If (t And TYPE_SETUP_CHANGE) <> 0 Then Return CLR_SETUP_CHANGE
        If (t And TYPE_MACHINE_OP) <> 0 Then Return GetMachineOpColor(id.OperationCategory)
        If (t And TYPE_TOOLPATH) <> 0 Then Return CLR_TOOLPATH
        If (t And TYPE_LINK) <> 0 Then Return CLR_LINK
        Return CLR_UNKNOWN
    End Function

    Private Shared Function GetMachineOpColor(category As Integer) As Color
        Select Case category
            Case TECH_CAT_TURNING : Return CLR_MACHINE_OP_TURNING
            Case TECH_CAT_MILLING : Return CLR_MACHINE_OP_MILLING
            Case Else : Return CLR_MACHINE_OP_OTHER
        End Select
    End Function

    Private Shared Function GetMachineOpLabel(category As Integer) As String
        Select Case category
            Case TECH_CAT_TURNING : Return Strings.Timeline_Legend_MachineOp_Turning
            Case TECH_CAT_MILLING : Return Strings.Timeline_Legend_MachineOp_Milling
            Case Else : Return Strings.Timeline_Legend_MachineOp_Other
        End Select
    End Function

    Private Function GetItemTypeName(id As ItemData) As String
        Dim t As Integer = id.ItemType
        If (t And TYPE_SYNC_NODE) <> 0 Then Return Strings.Timeline_Legend_Sync
        If (t And TYPE_TOOL_CHANGE) <> 0 Then Return Strings.Timeline_Legend_ToolChange
        If (t And TYPE_SETUP_CHANGE) <> 0 Then Return Strings.Timeline_Legend_SetupChange
        If (t And TYPE_MACHINE_OP) <> 0 Then Return GetMachineOpLabel(id.OperationCategory)
        If (t And TYPE_TOOLPATH) <> 0 Then Return Strings.Timeline_Legend_ToolPath
        If (t And TYPE_LINK) <> 0 Then Return Strings.Timeline_Legend_Link
        Return $"Type {t}"
    End Function

    Private Shared Function DarkenColor(c As Color, factor As Single) As Color
        Return Color.FromArgb(
            CInt(c.R * (1.0F - factor)),
            CInt(c.G * (1.0F - factor)),
            CInt(c.B * (1.0F - factor)))
    End Function

    Private Shared Function IsLightColor(c As Color) As Boolean
        Return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 > 0.6
    End Function

    ' ── Zoom ─────────────────────────────────────────────────────────────────

    Private Sub OnTimelineMouseWheel(sender As Object, e As MouseEventArgs)
        Dim oldDrawWidth As Integer = GetDrawWidth()
        ' Compute the time fraction under the cursor so we can keep it stable
        Dim cursorRelX As Integer = e.X - LABEL_WIDTH
        Dim timeFraction As Double = If(oldDrawWidth > 0 AndAlso cursorRelX > 0,
                                        cursorRelX / CDbl(oldDrawWidth), 0)

        Dim notches As Integer = e.Delta / 120
        Dim factor As Double = Math.Pow(1.2, notches)
        ' Upper bound keeps the panel width below GDI+ practical limits
        ' (MIN_DRAW_WIDTH × 50 ≈ 37,500 px).
        _zoomX = Math.Max(0.25, Math.Min(50.0, _zoomX * factor))

        UpdatePanelSize()

        ' Adjust horizontal scroll so the time position under the cursor stays put
        Dim newDrawWidth As Integer = GetDrawWidth()
        Dim newPanelX As Integer = LABEL_WIDTH + CInt(timeFraction * newDrawWidth)
        Dim newScrollX As Integer = Math.Max(0, newPanelX - e.X)
        _scrollPanel.AutoScrollPosition = New Point(newScrollX, -_scrollPanel.AutoScrollPosition.Y)

        UpdateZoomReadout()
        _timelinePanel.Invalidate()
    End Sub

    Private Sub OnZoomResetClick(sender As Object, e As EventArgs)
        If Math.Abs(_zoomX - 1.0) < 0.001 Then Return
        _zoomX = 1.0
        UpdatePanelSize()
        _scrollPanel.AutoScrollPosition = New Point(0, 0)
        UpdateZoomReadout()
        _timelinePanel.Invalidate()
    End Sub

    ''' <summary>
    ''' Refreshes the zoom label text with the current zoom percentage and
    ''' toggles the reset button state. Label and button stay visible at all
    ''' times; the button is disabled when already at 100%.
    ''' </summary>
    Private Sub UpdateZoomReadout()
        Dim zoomed As Boolean = Math.Abs(_zoomX - 1.0) > 0.001
        _zoomLabel.Text = $"    |    Zoom: {CInt(_zoomX * 100)}%"
        _zoomResetButton.Enabled = zoomed
        LayoutInfoRow()
    End Sub

    Private Sub OnInfoLabelTextChanged(sender As Object, e As EventArgs)
        LayoutInfoRow()
    End Sub

    ''' <summary>
    ''' Positions the zoom label and reset button immediately to the right of
    ''' the auto-sized info label so they sit next to the channel-count text.
    ''' </summary>
    Private Sub LayoutInfoRow()
        Const GAP As Integer = 4
        _zoomLabel.Location = New Point(_infoLabel.Right, 9)
        _zoomResetButton.Location = New Point(_zoomLabel.Right + GAP, 7)
    End Sub

    ' ── Control events ───────────────────────────────────────────────────────

    Private Sub OnRefreshClick(sender As Object, e As EventArgs)
        LoadData()
    End Sub

    Private Sub OnControlResize(sender As Object, e As EventArgs)
        UpdatePanelSize()
        _timelinePanel.Invalidate()
    End Sub

    ' ── Nested data model ─────────────────────────────────────────────────────

    Private Class ChannelData
        Public Name As String
        Public Index As Integer
        Public Items As New List(Of ItemData)
    End Class

    Private Class ItemData
        Public ItemType As Integer
        Public CycleTime As Double
        Public StartTime As Double
        Public ItemIndex As Long
        Public OperationName As String
        Public OperationCategory As Integer
    End Class

    Private Class SyncData
        Public SyncId As Integer
        Public Positions As New List(Of SyncPositionData)
    End Class

    Private Class SyncPositionData
        Public ChannelIndex As Integer
        Public ItemIndex As Long
        Public SyncTime As Double
    End Class

End Class
