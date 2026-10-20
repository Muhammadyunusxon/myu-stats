using Microsoft.UI.Dispatching;
using MYUStats.Interop;

namespace MYUStats.Edge;

/// <summary>
/// Owns the pill: places it on the chosen edge, tracks hover, and runs the settings button and the move
/// handle (slide along the edge, or drop onto another one).
/// </summary>
/// <remarks>
/// Hover is worked out from the cursor position against <see cref="EdgeLayout"/>, as on macOS. Pointer
/// events from the pill, the card and the edge strip trigger it; a 20 Hz poll runs only while something
/// can change without the mouse moving (a card or pill waiting to close, a button under the pointer,
/// the pill open with auto-hide), so a still mouse costs nothing.
/// </remarks>
internal sealed class EdgeController : IDisposable
{
    private static readonly TimeSpan CardGrace = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DragInterval = TimeSpan.FromMilliseconds(16);
    /// <summary>At launch the pill stays open this long, so it is clear where it lives.</summary>
    private static readonly TimeSpan Introduction = TimeSpan.FromSeconds(2.5);

    private readonly StatsStore store;
    private readonly AppSettings settings;
    private readonly Action openSettings;
    private readonly Action<StatMetric> openDetails;
    private readonly DispatcherQueue queue;
    private readonly PillWindow pill = new();
    private readonly CardWindow card = new();
    private readonly RevealStrip strip = new();
    private readonly Dictionary<ScreenEdge, DropZoneWindow> dropZones = [];
    private readonly UiDelay hide;
    private readonly UiDelay later;
    private readonly UiTimer poll;
    private readonly UiTimer dragTimer;

    private IReadOnlyList<StatMetric> metrics = [];
    private ScreenEdge builtEdge;
    private EdgeLayout layout = new(ScreenEdge.Right, 0, new SizeD(1, 1));
    private DisplayInfo? display;
    private bool isRevealed;
    private bool isPolling;
    private StatMetric? hovered;
    private PillButton? pointerButton;
    /// <summary>Cursor position and offset along the edge when a drag of the move handle began.</summary>
    private (PointD Mouse, double Offset)? drag;
    private double cardHeight = 300;
    private DateTimeOffset introductionEnds;
    private bool disposed;

    public EdgeController(StatsStore store, AppSettings settings, Action openSettings, Action<StatMetric> openDetails)
    {
        this.store = store;
        this.settings = settings;
        this.openSettings = openSettings;
        this.openDetails = openDetails;
        queue = DispatcherQueue.GetForCurrentThread();
        hide = new UiDelay(queue);
        later = new UiDelay(queue);
        poll = new UiTimer(queue);
        dragTimer = new UiTimer(queue);
    }

    public void Show()
    {
        pill.PointerActivity += EvaluateHover;
        pill.ScaleChanged += Reposition;
        card.PointerActivity += EvaluateHover;
        card.ScaleChanged += () =>
        {
            if (hovered is { } metric) PlaceCard(metric);
        };
        card.DetailsRequested += metric =>
        {
            SetHovered(null);
            openDetails(metric);
        };
        // Deferred: the strip reports from inside its window procedure.
        strip.Hovered += () => queue.TryEnqueue(EvaluateHover);
        strip.ScreenChanged += () => queue.TryEnqueue(Reposition);
        store.Changed += Refresh;
        store.SensorsChanged += Reposition;
        store.Claude.Changed += Refresh;
        store.Claude.AvailabilityChanged += Reposition;
        settings.Changed += OnSettingsChanged;

        ApplySurface();
        // Out on the edge once the first reading is in, so the battery and thermal rings are there from
        // the start instead of joining a moment later; folded after a short introduction.
        if (store.HasSampled)
        {
            Introduce();
            return;
        }
        void FirstSample()
        {
            store.Changed -= FirstSample;
            Introduce();
        }
        store.Changed += FirstSample;
    }

    private void Introduce()
    {
        isRevealed = true;
        introductionEnds = DateTimeOffset.Now + Introduction;
        Reposition();
        pill.View?.Unfold(settings.Edge);
    }

    /// <summary>Builds the pill and card again, for a new language.</summary>
    public void Rebuild()
    {
        metrics = [];
        Reposition();
        if (hovered is { } metric)
        {
            card.Show(metric, store, settings);
            PlaceCard(metric);
        }
    }

    private void Refresh()
    {
        // A folded pill is off screen: updating its rings would lay out a window nobody sees.
        if (isRevealed) pill.View?.Update(store);
        if (card.Metric is null) return;
        card.Refresh();
        if (hovered is { } metric && Math.Abs(card.Measure() - cardHeight) > 0.5) PlaceCard(metric);
    }

    private void OnSettingsChanged(string name)
    {
        switch (name)
        {
            case nameof(AppSettings.Surface):
            case nameof(AppSettings.Darkness):
                ApplySurface();
                break;
            case nameof(AppSettings.ShowProcesses):
                UpdateProcessDemand();
                Refresh();
                break;
            case nameof(AppSettings.HideDelay):
            case nameof(AppSettings.SampleInterval):
            case nameof(AppSettings.Language):
                // The app controller handles the interval and the language.
                break;
            case "*":
                ApplySurface();
                Rebuild();
                break;
            default:
                Reposition();
                break;
        }
    }

    private void ApplySurface()
    {
        pill.ApplySurface(settings.Surface, settings.Darkness);
        card.ApplySurface(settings.Surface, settings.Darkness);
        foreach (var zone in dropZones.Values) zone.ApplySurface(settings.Surface, settings.Darkness);
    }

    // MARK: - Placement

    private void Reposition() => Reposition(refreshDisplay: true);

    /// <param name="refreshDisplay">False while dragging: the display cannot change mid-drag, and
    /// listing the displays sixty times a second would stutter.</param>
    private void Reposition(bool refreshDisplay)
    {
        if (disposed) return;
        if (refreshDisplay || display is null) display = Displays.Chosen(settings.Display);
        if (display is null) return;

        var hidden = settings.Hidden;
        var visible = settings.Order.Where(metric => !hidden.Contains(metric) && store.IsAvailable(metric)).ToList();
        if (pill.View is null || builtEdge != settings.Edge || !visible.SequenceEqual(metrics))
        {
            metrics = visible;
            builtEdge = settings.Edge;
            var view = pill.Build(metrics, settings.Edge);
            Wire(view);
            view.Update(store);
            SetPointerButton(null, force: true);
        }
        layout = new EdgeLayout(settings.Edge, metrics.Count, display.WorkAreaDips);
        UpdateClaudeVisibility();
        if (hovered is { } current && !metrics.Contains(current)) SetHovered(null);

        if (metrics.Count == 0)
        {
            pill.Hide();
            strip.Hide();
            card.Hide();
            return;
        }
        var offset = CurrentOffset();
        if (isRevealed)
        {
            pill.Place(display.ToPixels(layout.PillRect(offset)));
            strip.Hide();
        }
        else
        {
            pill.Hide();
            strip.Show(display.ToPixels(layout.StripRect(offset)), display.ToPixels(layout.TabRect(offset)));
        }
        if (hovered is { } metric) PlaceCard(metric);
        // The pill may have moved under a still cursor.
        EvaluateHover();
    }

    private void Wire(PillView view)
    {
        view.MoveButton.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(view.MoveButton).Properties.IsLeftButtonPressed) return;
            view.MoveButton.CapturePointer(args.Pointer);
            args.Handled = true;
            StartDrag();
        };
        view.MoveButton.PointerReleased += (_, _) =>
        {
            view.MoveButton.ReleasePointerCaptures();
            FinishDrag();
        };
        view.MoveButton.PointerCaptureLost += (_, _) => FinishDrag();
        view.SettingsButton.Tapped += (_, _) =>
        {
            view.SettingsButton.Spin();
            SetHovered(null);
            // Let the gear start turning before the window takes the stage.
            later.Run(TimeSpan.FromMilliseconds(180), openSettings);
        };
        foreach (var cell in view.Cells)
        {
            cell.Tapped += (_, _) =>
            {
                SetHovered(null);
                openDetails(cell.Metric);
            };
        }
    }

    private double CurrentOffset()
    {
        if (drag is not { } start) return settings.Offset;
        var mouse = CursorDips();
        return start.Offset + (layout.IsVertical ? mouse.Y - start.Mouse.Y : mouse.X - start.Mouse.X);
    }

    private PointD CursorDips()
    {
        if (display is null || !NativeMethods.GetCursorPos(out var point)) return new PointD(double.NaN, double.NaN);
        return display.ToDips(point.X, point.Y);
    }

    private int? IndexOf(StatMetric metric)
    {
        for (var index = 0; index < metrics.Count; index++)
        {
            if (metrics[index] == metric) return index;
        }
        return null;
    }

    // MARK: - Hover

    private void EvaluateHover()
    {
        if (disposed || drag is not null || display is null || metrics.Count == 0)
        {
            RefreshPolling();
            return;
        }
        var mouse = CursorDips();
        var offset = CurrentOffset();
        if (!settings.AutoHide)
        {
            SetRevealed(true);
        }
        else if (!isRevealed)
        {
            SetPointerButton(null);
            if (layout.StripRect(offset).Inflate(1).Contains(mouse)) SetRevealed(true);
            RefreshPolling();
            return;
        }

        var button = layout.ButtonAt(offset, mouse);
        SetPointerButton(button);
        if (button is not null)
        {
            CancelHide();
            SetHovered(null);
            RefreshPolling();
            return;
        }

        if (layout.CellAt(offset, mouse) is { } index)
        {
            CancelHide();
            SetHovered(metrics[index]);
            RefreshPolling();
            return;
        }

        var inside = layout.PillRect(offset).Contains(mouse);
        if (hovered is { } metric && IndexOf(metric) is { } cardIndex)
        {
            inside |= layout.CardRect(offset, cardIndex, cardHeight).Inflate(4).Contains(mouse);
        }
        if (inside) CancelHide();
        else ScheduleHide(settings.AutoHide);
        RefreshPolling();
    }

    /// <summary>Polls only while something can change without the mouse moving.</summary>
    private void RefreshPolling()
    {
        var needed = !disposed && drag is null
            && (hide.IsPending || hovered is not null || pointerButton is not null || (isRevealed && settings.AutoHide));
        if (needed == isPolling) return;
        isPolling = needed;
        if (needed) poll.Start(PollInterval, EvaluateHover);
        else poll.Stop();
    }

    /// <summary>Closes the card after a short grace period, and with auto-hide also tucks the pill away.</summary>
    private void ScheduleHide(bool foldPill)
    {
        if (hide.IsPending || !(hovered is not null || (foldPill && isRevealed))) return;
        var delay = hovered is not null ? CardGrace : TimeSpan.FromSeconds(settings.HideDelay);
        var introduction = introductionEnds - DateTimeOffset.Now;
        if (hovered is null && introduction > delay) delay = introduction;
        hide.Run(delay, () =>
        {
            // Card first; the follow-up check schedules the pill with the user's own delay.
            if (hovered is not null) SetHovered(null);
            else if (foldPill) SetRevealed(false);
            EvaluateHover();
        });
    }

    private void CancelHide() => hide.Cancel();

    private void SetRevealed(bool revealed)
    {
        if (isRevealed == revealed) return;
        isRevealed = revealed;
        if (!revealed) SetHovered(null);
        // The rings were left as they were when the pill folded.
        if (revealed) pill.View?.Update(store);
        Reposition();
        if (revealed) pill.View?.Unfold(settings.Edge);
    }

    /// <summary>Claude Code's limits are read only while their ring can be seen; see
    /// <see cref="ClaudeUsageStore.IsOnScreen"/>.</summary>
    private void UpdateClaudeVisibility() =>
        store.Claude.IsOnScreen = isRevealed && metrics.Contains(StatMetric.Claude);

    private void SetPointerButton(PillButton? button, bool force = false)
    {
        if (pointerButton == button && !force) return;
        pointerButton = button;
        if (pill.View is not { } view) return;
        view.MoveButton.IsHighlighted = button == PillButton.Move || drag is not null;
        view.SettingsButton.IsHighlighted = button == PillButton.Settings;
    }

    private void SetHovered(StatMetric? metric)
    {
        if (hovered == metric) return;
        var opening = hovered is null;
        hovered = metric;
        foreach (var cell in pill.View?.Cells ?? []) cell.IsHovered = cell.Metric == metric;
        UpdateProcessDemand();
        if (metric is not { } shown)
        {
            card.Hide();
            return;
        }
        if (shown == StatMetric.Claude) store.Claude.RefreshIfStale();
        card.Show(shown, store, settings);
        PlaceCard(shown);
        if (opening) card.Appear(settings.Edge);
    }

    private void PlaceCard(StatMetric metric)
    {
        if (display is null || IndexOf(metric) is not { } index) return;
        var measured = card.Measure();
        // A card that has never been on screen can measure short; the next sample corrects it.
        cardHeight = measured > 40 ? measured : cardHeight;
        card.Place(display.ToPixels(layout.CardRect(CurrentOffset(), index, cardHeight)));
    }

    private void UpdateProcessDemand() =>
        store.SetProcessSampling(hovered is StatMetric.Cpu or StatMetric.Memory && settings.ShowProcesses, "card");

    // MARK: - Dragging

    private void StartDrag()
    {
        if (drag is not null) return;
        drag = (CursorDips(), settings.Offset);
        CancelHide();
        SetHovered(null);
        SetPointerButton(PillButton.Move, force: true);
        RefreshPolling();
        dragTimer.Start(DragInterval, DragTick);
    }

    private void DragTick()
    {
        if (drag is null)
        {
            dragTimer.Stop();
            return;
        }
        // The release can be missed if something else takes the mouse; the button state is the truth.
        if (!NativeMethods.IsPrimaryButtonDown())
        {
            FinishDrag();
            return;
        }
        Reposition(refreshDisplay: false);
        UpdateDropZones();
    }

    private void UpdateDropZones()
    {
        if (display is null) return;
        var mouse = CursorDips();
        var area = display.WorkAreaDips;
        var target = EdgeLayout.DropTarget(mouse, area, layout.Edge);
        foreach (var edge in ScreenEdges.All)
        {
            if (edge == layout.Edge)
            {
                if (dropZones.TryGetValue(edge, out var own)) own.Hide();
                continue;
            }
            var zone = DropZone(edge);
            zone.IsArmed = edge == target;
            zone.Place(display.ToPixels(EdgeLayout.DropZoneRect(edge, mouse, area)));
        }
        pill.IsLeaving = target is not null;
    }

    private DropZoneWindow DropZone(ScreenEdge edge)
    {
        if (dropZones.TryGetValue(edge, out var zone)) return zone;
        zone = new DropZoneWindow(edge);
        zone.ApplySurface(settings.Surface, settings.Darkness);
        dropZones[edge] = zone;
        return zone;
    }

    private void FinishDrag()
    {
        if (drag is null) return;
        dragTimer.Stop();
        var mouse = CursorDips();
        // Store where the pill actually ended up, so a drag that hit the end of the edge does not drift later.
        var landed = layout.OffsetOf(layout.PillRect(CurrentOffset()));
        var target = display is null ? null : EdgeLayout.DropTarget(mouse, display.WorkAreaDips, layout.Edge);
        drag = null;
        foreach (var zone in dropZones.Values) zone.Hide();
        pill.IsLeaving = false;
        SetPointerButton(null, force: true);
        if (target is { } edge && display is not null)
        {
            // Land centred on the cursor on the new edge.
            settings.Offset = EdgeLayout.LandingOffset(edge, mouse, display.WorkAreaDips);
            settings.Edge = edge;
        }
        else
        {
            settings.Offset = landed;
        }
        Reposition();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        poll.Stop();
        dragTimer.Stop();
        hide.Cancel();
        later.Cancel();
        store.Changed -= Refresh;
        store.SensorsChanged -= Reposition;
        store.Claude.Changed -= Refresh;
        store.Claude.AvailabilityChanged -= Reposition;
        settings.Changed -= OnSettingsChanged;
        strip.Dispose();
    }
}
