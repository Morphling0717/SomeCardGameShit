// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.CardFaces;
using Scgs.GodotClient.Ci;

namespace Scgs.GodotClient.Preview;

/// <summary>
/// No-native approval surface for the integrated AnimeV1 card body. Every card
/// below is a real CardActor3D rendered directly in the main viewport.
/// </summary>
public sealed partial class AnimeCardBodySliceScreen : Control
{
    private readonly List<CardActor3D> _actors = [];
    private AnimeCardBodySliceLaunch _launch =
        new(false, null, false, AnimeCardBodySliceLaunch.StateRepresentatives);
    private Node3D _stage = null!;
    private Camera3D _camera = null!;
    private Label _title = null!;
    private HBoxContainer _toolbar = null!;
    private string _state = AnimeCardBodySliceLaunch.StateRepresentatives;
    private bool _ready;
    private bool _captureStarted;

    internal string CurrentState => _state;

    internal void Configure(AnimeCardBodySliceLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (_ready)
        {
            throw new InvalidOperationException("Configure the card-body slice before it enters the tree.");
        }
        _launch = launch;
    }

    public override void _Ready()
    {
        _ready = true;
        MouseFilter = MouseFilterEnum.Pass;
        BuildWorld();
        BuildOverlay();
        SetPreviewState(_launch.InitialState);
        if (_launch.OutputDirectory is not null)
        {
            _toolbar.Visible = false;
            Callable.From(RunCaptureSuite).CallDeferred();
        }
    }

    internal void SetPreviewState(string state)
    {
        if (!AnimeCardBodySliceLaunch.States.Contains(state, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown card-body approval state.");
        }
        _state = state;
        ClearActors();
        _title.Text = state switch
        {
            AnimeCardBodySliceLaunch.StateContact => "五类 × 三阵营 × 四级稀有度",
            AnimeCardBodySliceLaunch.StateRepresentatives => "代表卡与进化异画",
            AnimeCardBodySliceLaunch.StateContexts => "详情／手牌／场上共用同一组合",
            AnimeCardBodySliceLaunch.StateHandOne => "一张手牌",
            AnimeCardBodySliceLaunch.StateHandFive => "五张手牌",
            AnimeCardBodySliceLaunch.StateHandTen => "十张手牌",
            AnimeCardBodySliceLaunch.StateHandHover => "手牌悬停与相邻让位",
            _ => "费用、身材、受伤与倒数可读性",
        };

        switch (state)
        {
            case AnimeCardBodySliceLaunch.StateContact:
                BuildContactSheet();
                break;
            case AnimeCardBodySliceLaunch.StateRepresentatives:
                BuildRepresentatives();
                break;
            case AnimeCardBodySliceLaunch.StateContexts:
                BuildContexts();
                break;
            case AnimeCardBodySliceLaunch.StateHandOne:
                BuildHand(1, hoveredIndex: null);
                break;
            case AnimeCardBodySliceLaunch.StateHandFive:
                BuildHand(5, hoveredIndex: null);
                break;
            case AnimeCardBodySliceLaunch.StateHandTen:
                BuildHand(10, hoveredIndex: null);
                break;
            case AnimeCardBodySliceLaunch.StateHandHover:
                BuildHand(5, hoveredIndex: 2);
                break;
            case AnimeCardBodySliceLaunch.StateValues:
                BuildValueCases();
                break;
        }
        _toolbar.MoveToFront();
        _title.MoveToFront();
    }

    internal AnimeCardBodySliceEvidence MeasureEvidence()
    {
        CardFaceComposition[] faces = _actors
            .Select(actor => actor.CiProductFace)
            .Where(face => face is not null)
            .Cast<CardFaceComposition>()
            .ToArray();
        return new AnimeCardBodySliceEvidence
        {
            State = _state,
            ActorCount = _actors.Count,
            IntegratedActorCount = _actors.Count(actor => actor.CiUsesIntegratedProductFace),
            DistinctStyleCount = faces.Select(face => face.FrameStyle.Key).Distinct().Count(),
            Contexts = faces.Select(face => face.Layout.Context.ToString())
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DesignIds = faces.Select(face => face.ViewModel.DesignId)
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SubViewportCount = CountNodes<SubViewport>(this),
            UsesNativeSession = false,
        };
    }

    internal void SetGpuValueLabelsVisible(bool visible)
    {
        foreach (CardActor3D actor in _actors)
        {
            actor.CiSetProductValueLabelsVisible(visible);
        }
    }

    internal IReadOnlyList<string> GetGpuReferenceActorNames() =>
        _actors.Select(actor => actor.Name.ToString()).ToArray();

    internal void SetGpuValueLabelsVisibleForActor(string actorName, bool visible)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            throw new ArgumentException("Actor name is required.", nameof(actorName));
        }

        CardActor3D actor = _actors.SingleOrDefault(candidate =>
            string.Equals(candidate.Name.ToString(), actorName, StringComparison.Ordinal)) ??
            throw new ArgumentOutOfRangeException(
                nameof(actorName),
                actorName,
                "Unknown card actor for GPU readability capture.");
        actor.CiSetProductValueLabelsVisible(visible);
    }

    internal void SetGpuNameLabelsVisible(bool visible)
    {
        foreach (CardActor3D actor in _actors)
        {
            actor.CiSetProductNameLabelVisible(visible);
        }
    }

    internal void SetGpuNameLabelVisibleForActor(string actorName, bool visible)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            throw new ArgumentException("Actor name is required.", nameof(actorName));
        }

        CardActor3D actor = _actors.SingleOrDefault(candidate =>
            string.Equals(candidate.Name.ToString(), actorName, StringComparison.Ordinal)) ??
            throw new ArgumentOutOfRangeException(
                nameof(actorName),
                actorName,
                "Unknown card actor for GPU name capture.");
        actor.CiSetProductNameLabelVisible(visible);
    }

    internal void SetProductFaceLayersVisible(bool visible)
    {
        foreach (CardActor3D actor in _actors)
        {
            actor.CiSetProductLayersVisible(visible);
        }
    }

    internal IReadOnlyList<string> GetSilhouetteReferenceActorNames() =>
        _actors.Select(actor => actor.Name.ToString()).ToArray();

    internal void SetProductFaceLayersVisibleForActor(string actorName, bool visible)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            throw new ArgumentException("Actor name is required.", nameof(actorName));
        }

        CardActor3D actor = _actors.SingleOrDefault(candidate =>
            string.Equals(candidate.Name.ToString(), actorName, StringComparison.Ordinal)) ??
            throw new ArgumentOutOfRangeException(
                nameof(actorName),
                actorName,
                "Unknown card actor for silhouette capture.");
        actor.CiSetProductLayersVisible(visible);
    }

    internal AnimeCardBodyGpuReadabilityEvidence MeasureGpuReadability(
        Image frame,
        IReadOnlyDictionary<string, Image> framesWithoutActorValueLabels,
        IReadOnlyDictionary<string, Image> framesWithoutActorNameLabels)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(framesWithoutActorValueLabels);
        ArgumentNullException.ThrowIfNull(framesWithoutActorNameLabels);
        int minimumPixelHeight =
            AnimeCardBodyGpuReadabilityPolicy.MinimumBadgePixelHeight(frame.GetHeight());
        Vector2 projectionScale = ProjectionToFrameScale(frame);
        bool required = AnimeCardBodyGpuReadabilityPolicy.RequiresEvidence(_state);
        if (!required)
        {
            return new AnimeCardBodyGpuReadabilityEvidence
            {
                State = _state,
                Required = false,
                MinimumBadgePixelHeight = minimumPixelHeight,
                ViewportWidth = frame.GetWidth(),
                ViewportHeight = frame.GetHeight(),
                ActorCount = 0,
                RequiredBadgeCount = 0,
                RequiredNameCount = 0,
                CompleteNameCount = 0,
                AllRequiredBadgesReadable = true,
                AllRequiredNamesReadable = true,
                Actors = [],
            };
        }

        var actorEvidence = new List<AnimeCardBodyActorGpuEvidence>(_actors.Count);
        int requiredBadgeCount = 0;
        foreach (CardActor3D actor in _actors)
        {
            string actorName = actor.Name.ToString();
            if (!framesWithoutActorValueLabels.TryGetValue(actorName, out Image? reference))
            {
                throw new ArgumentException(
                    $"GPU readability reference is missing for actor {actorName}.",
                    nameof(framesWithoutActorValueLabels));
            }
            if (frame.GetWidth() != reference.GetWidth() ||
                frame.GetHeight() != reference.GetHeight())
            {
                throw new ArgumentException(
                    $"GPU readability reference for actor {actorName} has different dimensions.",
                    nameof(framesWithoutActorValueLabels));
            }
            if (!framesWithoutActorNameLabels.TryGetValue(actorName, out Image? nameReference))
            {
                throw new ArgumentException(
                    $"GPU name reference is missing for actor {actorName}.",
                    nameof(framesWithoutActorNameLabels));
            }
            if (frame.GetWidth() != nameReference.GetWidth() ||
                frame.GetHeight() != nameReference.GetHeight())
            {
                throw new ArgumentException(
                    $"GPU name reference for actor {actorName} has different dimensions.",
                    nameof(framesWithoutActorNameLabels));
            }
            CardFaceComposition composition = actor.CiProductFace ??
                throw new InvalidOperationException(
                    $"Real actor {actor.Name} lost its product composition before GPU evidence.");
            CardGpuReadabilityEvidence gpu = actor.CiGpuReadabilityEvidence(_camera);
            var badges = new List<AnimeCardBodyBadgeGpuEvidence>
            {
                MeasureBadge(frame, reference, projectionScale, actorName, "cost", gpu.CostBadge, minimumPixelHeight),
            };
            if (gpu.Local.AttackExpected)
            {
                badges.Add(MeasureBadge(frame, reference, projectionScale, actorName, "attack", gpu.AttackBadge, minimumPixelHeight));
            }
            if (gpu.Local.HealthExpected)
            {
                badges.Add(MeasureBadge(frame, reference, projectionScale, actorName, "health", gpu.HealthBadge, minimumPixelHeight));
            }
            if (gpu.Local.CountdownExpected)
            {
                badges.Add(MeasureBadge(frame, reference, projectionScale, actorName, "countdown", gpu.CountdownBadge, minimumPixelHeight));
            }

            bool allReadable = badges.All(badge => badge.Readable);
            AnimeCardBodyNameGpuEvidence name = MeasureName(
                frame,
                nameReference,
                projectionScale,
                actorName,
                composition.ViewModel.DisplayName,
                actor.CiProductNameGpuEvidence(_camera));
            requiredBadgeCount += badges.Count;
            actorEvidence.Add(new AnimeCardBodyActorGpuEvidence
            {
                ActorName = actor.Name,
                DesignId = composition.ViewModel.DesignId,
                ProductKind = composition.ViewModel.Kind.ToString(),
                LocalCompositionReadable =
                    gpu.MatchesExpectedComposition(minimumPixelHeight / projectionScale.Y),
                RequiredBadgeCount = badges.Count,
                AllRequiredBadgesReadable = allReadable,
                NameReadable = name.Readable,
                Badges = badges,
                Name = name,
            });
        }

        bool allBadgesReadable = actorEvidence.All(actor =>
            actor.LocalCompositionReadable && actor.AllRequiredBadgesReadable);
        bool allNamesReadable = actorEvidence.All(actor => actor.NameReadable);
        return new AnimeCardBodyGpuReadabilityEvidence
        {
            State = _state,
            Required = true,
            MinimumBadgePixelHeight = minimumPixelHeight,
            ViewportWidth = frame.GetWidth(),
            ViewportHeight = frame.GetHeight(),
            ActorCount = actorEvidence.Count,
            RequiredBadgeCount = requiredBadgeCount,
            RequiredNameCount = actorEvidence.Count,
            CompleteNameCount = actorEvidence.Count(actor => actor.Name.FullNameMatchesSource),
            AllRequiredBadgesReadable = allBadgesReadable,
            AllRequiredNamesReadable = allNamesReadable,
            Actors = actorEvidence,
        };
    }

    private static AnimeCardBodyBadgeGpuEvidence MeasureBadge(
        Image frame,
        Image frameWithoutActorValueLabels,
        Vector2 projectionScale,
        string referenceActorName,
        string role,
        CardBadgeGpuEvidence badge,
        int minimumPixelHeight)
    {
        Rect2 screen = ScaleProjectionRect(badge.ScreenRect, projectionScale);
        Rect2 socket = ScaleProjectionRect(badge.SocketScreenRect, projectionScale);
        bool finite = float.IsFinite(screen.Position.X) &&
                      float.IsFinite(screen.Position.Y) &&
                      float.IsFinite(screen.Size.X) &&
                      float.IsFinite(screen.Size.Y);
        int frameWidth = frame.GetWidth();
        int frameHeight = frame.GetHeight();
        bool fullyInside = finite && screen.Size.X > 0.0f && screen.Size.Y > 0.0f &&
                           screen.Position.X >= 0.0f && screen.Position.Y >= 0.0f &&
                           screen.End.X <= frameWidth && screen.End.Y <= frameHeight;
        bool socketFinite = IsFiniteRect(socket);
        bool socketFullyInside = socketFinite &&
                                 socket.Size.X > 0.0f && socket.Size.Y > 0.0f &&
                                 socket.Position.X >= 0.0f && socket.Position.Y >= 0.0f &&
                                 socket.End.X <= frameWidth && socket.End.Y <= frameHeight;

        int left = finite
            ? Math.Clamp((int)MathF.Floor(screen.Position.X), 0, frameWidth)
            : 0;
        int top = finite
            ? Math.Clamp((int)MathF.Floor(screen.Position.Y), 0, frameHeight)
            : 0;
        int right = finite
            ? Math.Clamp((int)MathF.Ceiling(screen.End.X), 0, frameWidth)
            : 0;
        int bottom = finite
            ? Math.Clamp((int)MathF.Ceiling(screen.End.Y), 0, frameHeight)
            : 0;
        int brightPixels = 0;
        int glyphDifferencePixels = 0;
        int brightGlyphDifferencePixels = 0;
        int highContrastGlyphDifferencePixels = 0;
        float maximumGlyphContrast = 0.0f;
        int glyphLeft = right;
        int glyphTop = bottom;
        int glyphRight = left - 1;
        int glyphBottom = top - 1;
        var colorBuckets = new HashSet<int>();
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                Color pixel = frame.GetPixel(x, y);
                Color withoutLabel = frameWithoutActorValueLabels.GetPixel(x, y);
                float channelDifference = MathF.Max(
                        MathF.Abs(pixel.R - withoutLabel.R),
                        MathF.Max(
                            MathF.Abs(pixel.G - withoutLabel.G),
                            MathF.Abs(pixel.B - withoutLabel.B)));
                if (channelDifference >= 0.075f)
                {
                    glyphDifferencePixels++;
                    glyphLeft = Math.Min(glyphLeft, x);
                    glyphTop = Math.Min(glyphTop, y);
                    glyphRight = Math.Max(glyphRight, x);
                    glyphBottom = Math.Max(glyphBottom, y);

                    float pixelLuminance =
                        (pixel.R * 0.2126f) + (pixel.G * 0.7152f) + (pixel.B * 0.0722f);
                    float referenceLuminance =
                        (withoutLabel.R * 0.2126f) +
                        (withoutLabel.G * 0.7152f) +
                        (withoutLabel.B * 0.0722f);
                    float contrast = MathF.Abs(pixelLuminance - referenceLuminance);
                    maximumGlyphContrast = Math.Max(maximumGlyphContrast, contrast);
                    if (contrast >= 0.18f)
                    {
                        highContrastGlyphDifferencePixels++;
                    }
                }
                if (pixel.A < 0.50f)
                {
                    continue;
                }
                int red = Math.Clamp((int)MathF.Round(pixel.R * 255.0f), 0, 255);
                int green = Math.Clamp((int)MathF.Round(pixel.G * 255.0f), 0, 255);
                int blue = Math.Clamp((int)MathF.Round(pixel.B * 255.0f), 0, 255);
                colorBuckets.Add(((red >> 5) << 6) | ((green >> 5) << 3) | (blue >> 5));
                int maximum = Math.Max(red, Math.Max(green, blue));
                int minimum = Math.Min(red, Math.Min(green, blue));
                if (minimum >= 174 && maximum - minimum <= 64)
                {
                    brightPixels++;
                    // This pixel must be both label-specific (on/off delta)
                    // and bright/near-neutral in the final on-frame. Unlike
                    // separate ROI brightness and variance checks, a bright
                    // gem highlight cannot satisfy this by itself.
                    if (channelDifference >= 0.075f)
                    {
                        brightGlyphDifferencePixels++;
                    }
                }
            }
        }

        int glyphWidth = glyphRight >= glyphLeft ? glyphRight - glyphLeft + 1 : 0;
        int glyphHeight = glyphBottom >= glyphTop ? glyphBottom - glyphTop + 1 : 0;
        float glyphInsetLeft = glyphWidth > 0 ? glyphLeft - socket.Position.X : 0.0f;
        float glyphInsetTop = glyphHeight > 0 ? glyphTop - socket.Position.Y : 0.0f;
        float glyphInsetRight = glyphWidth > 0 ? socket.End.X - (glyphRight + 1.0f) : 0.0f;
        float glyphInsetBottom = glyphHeight > 0 ? socket.End.Y - (glyphBottom + 1.0f) : 0.0f;
        bool glyphInsideSocket = socketFullyInside && glyphWidth > 0 && glyphHeight > 0 &&
                                 glyphInsetLeft >= 1.0f && glyphInsetTop >= 1.0f &&
                                 glyphInsetRight >= 1.0f && glyphInsetBottom >= 1.0f;

        var measured = new AnimeCardBodyBadgeGpuEvidence
        {
            Role = role,
            Text = badge.Local.Text,
            ReferenceActorName = referenceActorName,
            Expected = true,
            ScreenX = finite ? screen.Position.X : 0.0f,
            ScreenY = finite ? screen.Position.Y : 0.0f,
            ScreenWidth = finite ? screen.Size.X : 0.0f,
            ScreenHeight = finite ? screen.Size.Y : 0.0f,
            PixelHeight = finite ? Math.Max(0, (int)MathF.Floor(screen.Size.Y)) : 0,
            FullyInsideViewport = fullyInside,
            RoiX = left,
            RoiY = top,
            RoiWidth = Math.Max(0, right - left),
            RoiHeight = Math.Max(0, bottom - top),
            BrightPixelCount = brightPixels,
            ColorBucketCount = colorBuckets.Count,
            GlyphDifferencePixelCount = glyphDifferencePixels,
            BrightGlyphDifferencePixelCount = brightGlyphDifferencePixels,
            SocketScreenX = socketFinite ? socket.Position.X : 0.0f,
            SocketScreenY = socketFinite ? socket.Position.Y : 0.0f,
            SocketScreenWidth = socketFinite ? socket.Size.X : 0.0f,
            SocketScreenHeight = socketFinite ? socket.Size.Y : 0.0f,
            SocketFullyInsideViewport = socketFullyInside,
            RequiredSocketInsetPixels = 1,
            GlyphSocketInsetLeft = glyphInsetLeft,
            GlyphSocketInsetTop = glyphInsetTop,
            GlyphSocketInsetRight = glyphInsetRight,
            GlyphSocketInsetBottom = glyphInsetBottom,
            GlyphInsideSocket = glyphInsideSocket,
            GlyphRoiX = glyphWidth > 0 ? glyphLeft : left,
            GlyphRoiY = glyphHeight > 0 ? glyphTop : top,
            GlyphPixelWidth = glyphWidth,
            GlyphPixelHeight = glyphHeight,
            HighContrastGlyphDifferencePixelCount = highContrastGlyphDifferencePixels,
            MaximumGlyphContrast = maximumGlyphContrast,
            Readable = false,
        };
        return measured with
        {
            Readable = AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
                measured,
                minimumPixelHeight),
        };
    }

    private static AnimeCardBodyNameGpuEvidence MeasureName(
        Image frame,
        Image frameWithoutActorName,
        Vector2 projectionScale,
        string referenceActorName,
        string sourceText,
        CardNameGpuEvidence name)
    {
        Rect2 screen = ScaleProjectionRect(name.ScreenRect, projectionScale);
        Rect2 textSocket = ScaleProjectionRect(name.TextSocketScreenRect, projectionScale);
        Rect2 namePlate = ScaleProjectionRect(name.NamePlateScreenRect, projectionScale);
        int frameWidth = frame.GetWidth();
        int frameHeight = frame.GetHeight();
        bool screenInside = IsRectFullyInside(screen, frameWidth, frameHeight);
        bool textSocketInside = IsRectFullyInside(textSocket, frameWidth, frameHeight);
        bool namePlateInside = IsRectFullyInside(namePlate, frameWidth, frameHeight);

        int left = IsFiniteRect(screen)
            ? Math.Clamp((int)MathF.Floor(screen.Position.X), 0, frameWidth)
            : 0;
        int top = IsFiniteRect(screen)
            ? Math.Clamp((int)MathF.Floor(screen.Position.Y), 0, frameHeight)
            : 0;
        int right = IsFiniteRect(screen)
            ? Math.Clamp((int)MathF.Ceiling(screen.End.X), 0, frameWidth)
            : 0;
        int bottom = IsFiniteRect(screen)
            ? Math.Clamp((int)MathF.Ceiling(screen.End.Y), 0, frameHeight)
            : 0;
        int glyphDifferencePixels = 0;
        int brightGlyphDifferencePixels = 0;
        int highContrastGlyphDifferencePixels = 0;
        float maximumGlyphContrast = 0.0f;
        int glyphLeft = right;
        int glyphTop = bottom;
        int glyphRight = left - 1;
        int glyphBottom = top - 1;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                Color pixel = frame.GetPixel(x, y);
                Color reference = frameWithoutActorName.GetPixel(x, y);
                float difference = MathF.Max(
                    MathF.Abs(pixel.R - reference.R),
                    MathF.Max(
                        MathF.Abs(pixel.G - reference.G),
                        MathF.Abs(pixel.B - reference.B)));
                if (difference < 0.075f)
                {
                    continue;
                }

                glyphDifferencePixels++;
                glyphLeft = Math.Min(glyphLeft, x);
                glyphTop = Math.Min(glyphTop, y);
                glyphRight = Math.Max(glyphRight, x);
                glyphBottom = Math.Max(glyphBottom, y);
                float pixelLuminance =
                    (pixel.R * 0.2126f) + (pixel.G * 0.7152f) + (pixel.B * 0.0722f);
                float referenceLuminance =
                    (reference.R * 0.2126f) +
                    (reference.G * 0.7152f) +
                    (reference.B * 0.0722f);
                float contrast = MathF.Abs(pixelLuminance - referenceLuminance);
                maximumGlyphContrast = Math.Max(maximumGlyphContrast, contrast);
                if (contrast >= 0.18f)
                {
                    highContrastGlyphDifferencePixels++;
                }
                int red = Math.Clamp((int)MathF.Round(pixel.R * 255.0f), 0, 255);
                int green = Math.Clamp((int)MathF.Round(pixel.G * 255.0f), 0, 255);
                int blue = Math.Clamp((int)MathF.Round(pixel.B * 255.0f), 0, 255);
                int maximum = Math.Max(red, Math.Max(green, blue));
                int minimum = Math.Min(red, Math.Min(green, blue));
                if (pixel.A >= 0.50f && minimum >= 174 && maximum - minimum <= 64)
                {
                    brightGlyphDifferencePixels++;
                }
            }
        }

        int glyphWidth = glyphRight >= glyphLeft ? glyphRight - glyphLeft + 1 : 0;
        int glyphHeight = glyphBottom >= glyphTop ? glyphBottom - glyphTop + 1 : 0;
        float glyphInsetLeft = glyphWidth > 0 ? glyphLeft - textSocket.Position.X : 0.0f;
        float glyphInsetTop = glyphHeight > 0 ? glyphTop - textSocket.Position.Y : 0.0f;
        float glyphInsetRight = glyphWidth > 0
            ? textSocket.End.X - (glyphRight + 1.0f)
            : 0.0f;
        float glyphInsetBottom = glyphHeight > 0
            ? textSocket.End.Y - (glyphBottom + 1.0f)
            : 0.0f;
        float socketPlateInsetLeft = textSocket.Position.X - namePlate.Position.X;
        float socketPlateInsetTop = textSocket.Position.Y - namePlate.Position.Y;
        float socketPlateInsetRight = namePlate.End.X - textSocket.End.X;
        float socketPlateInsetBottom = namePlate.End.Y - textSocket.End.Y;
        bool glyphInsideSocket = textSocketInside && glyphWidth > 0 && glyphHeight > 0 &&
                                 glyphInsetLeft >= 1.0f && glyphInsetTop >= 1.0f &&
                                 glyphInsetRight >= 1.0f && glyphInsetBottom >= 1.0f;
        bool socketInsidePlate = textSocketInside && namePlateInside &&
                                 socketPlateInsetLeft >= 1.0f && socketPlateInsetTop >= 1.0f &&
                                 socketPlateInsetRight >= 1.0f && socketPlateInsetBottom >= 1.0f;
        bool fullNameMatches =
            string.Equals(name.Text, sourceText, StringComparison.Ordinal) &&
            !name.Text.Contains('…') &&
            !sourceText.Contains('…');
        float maximumCenterDelta =
            AnimeCardBodyGpuReadabilityPolicy.MaximumNameCenterDeltaPixels(
                frameWidth,
                frameHeight);
        float minimumHorizontalPlateInset =
            AnimeCardBodyGpuReadabilityPolicy.MinimumNamePlateHorizontalInsetPixels(
                frameWidth,
                frameHeight);
        float glyphCenterDeltaX = glyphWidth > 0
            ? MathF.Abs(((glyphLeft + glyphRight + 1.0f) * 0.5f) - textSocket.GetCenter().X)
            : frameWidth;
        float glyphCenterDeltaY = glyphHeight > 0
            ? MathF.Abs(((glyphTop + glyphBottom + 1.0f) * 0.5f) - textSocket.GetCenter().Y)
            : frameHeight;
        bool glyphCentered = glyphCenterDeltaX <= maximumCenterDelta &&
                             glyphCenterDeltaY <= maximumCenterDelta;
        var measured = new AnimeCardBodyNameGpuEvidence
        {
            Text = name.Text,
            SourceText = sourceText,
            FullNameMatchesSource = fullNameMatches,
            ReferenceActorName = referenceActorName,
            Expected = true,
            FontSize = name.FontSize,
            ScreenX = IsFiniteRect(screen) ? screen.Position.X : 0.0f,
            ScreenY = IsFiniteRect(screen) ? screen.Position.Y : 0.0f,
            ScreenWidth = IsFiniteRect(screen) ? screen.Size.X : 0.0f,
            ScreenHeight = IsFiniteRect(screen) ? screen.Size.Y : 0.0f,
            ScreenFullyInsideViewport = screenInside,
            TextSocketScreenX = IsFiniteRect(textSocket) ? textSocket.Position.X : 0.0f,
            TextSocketScreenY = IsFiniteRect(textSocket) ? textSocket.Position.Y : 0.0f,
            TextSocketScreenWidth = IsFiniteRect(textSocket) ? textSocket.Size.X : 0.0f,
            TextSocketScreenHeight = IsFiniteRect(textSocket) ? textSocket.Size.Y : 0.0f,
            TextSocketFullyInsideViewport = textSocketInside,
            NamePlateScreenX = IsFiniteRect(namePlate) ? namePlate.Position.X : 0.0f,
            NamePlateScreenY = IsFiniteRect(namePlate) ? namePlate.Position.Y : 0.0f,
            NamePlateScreenWidth = IsFiniteRect(namePlate) ? namePlate.Size.X : 0.0f,
            NamePlateScreenHeight = IsFiniteRect(namePlate) ? namePlate.Size.Y : 0.0f,
            NamePlateFullyInsideViewport = namePlateInside,
            RequiredSocketInsetPixels = 1,
            RequiredNamePlateHorizontalInsetPixels = minimumHorizontalPlateInset,
            TextSocketNamePlateInsetLeft = socketPlateInsetLeft,
            TextSocketNamePlateInsetTop = socketPlateInsetTop,
            TextSocketNamePlateInsetRight = socketPlateInsetRight,
            TextSocketNamePlateInsetBottom = socketPlateInsetBottom,
            TextSocketInsideNamePlate = socketInsidePlate,
            RoiX = left,
            RoiY = top,
            RoiWidth = Math.Max(0, right - left),
            RoiHeight = Math.Max(0, bottom - top),
            GlyphDifferencePixelCount = glyphDifferencePixels,
            BrightGlyphDifferencePixelCount = brightGlyphDifferencePixels,
            GlyphRoiX = glyphWidth > 0 ? glyphLeft : left,
            GlyphRoiY = glyphHeight > 0 ? glyphTop : top,
            GlyphPixelWidth = glyphWidth,
            GlyphPixelHeight = glyphHeight,
            HighContrastGlyphDifferencePixelCount = highContrastGlyphDifferencePixels,
            MaximumGlyphContrast = maximumGlyphContrast,
            GlyphSocketInsetLeft = glyphInsetLeft,
            GlyphSocketInsetTop = glyphInsetTop,
            GlyphSocketInsetRight = glyphInsetRight,
            GlyphSocketInsetBottom = glyphInsetBottom,
            GlyphInsideTextSocket = glyphInsideSocket,
            MaximumGlyphSocketCenterDeltaPixels = maximumCenterDelta,
            GlyphSocketCenterDeltaX = glyphCenterDeltaX,
            GlyphSocketCenterDeltaY = glyphCenterDeltaY,
            GlyphCenteredInTextSocket = glyphCentered,
            Readable = false,
        };
        return measured with
        {
            Readable = AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
                measured,
                frameWidth,
                frameHeight),
        };
    }

    internal AnimeCardBodySilhouetteEvidence MeasureSilhouetteIsolation(
        Image frame,
        IReadOnlyDictionary<string, Image> framesWithoutActorProductLayers)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(framesWithoutActorProductLayers);
        bool required = AnimeCardBodySilhouettePolicy.RequiresEvidence(_state);
        if (!required)
        {
            return new AnimeCardBodySilhouetteEvidence
            {
                State = _state,
                Required = false,
                ActorCount = 0,
                ProbeCount = 0,
                InteriorProbeCount = 0,
                AllRectangularBasesHidden = true,
                AllCornerProbesMatchBackground = true,
                AllInteriorProbesShowProductFace = true,
                Probes = [],
                InteriorProbes = [],
            };
        }

        var probes = new List<AnimeCardBodySilhouetteProbeEvidence>(_actors.Count * 4);
        var interiorProbes = new List<AnimeCardBodyInteriorProbeEvidence>(_actors.Count);
        Vector2 projectionScale = ProjectionToFrameScale(frame);
        foreach (CardActor3D actor in _actors)
        {
            string actorName = actor.Name.ToString();
            if (!framesWithoutActorProductLayers.TryGetValue(actorName, out Image? referenceFrame))
            {
                throw new ArgumentException(
                    $"Silhouette reference is missing for actor {actorName}.",
                    nameof(framesWithoutActorProductLayers));
            }
            if (frame.GetWidth() != referenceFrame.GetWidth() ||
                frame.GetHeight() != referenceFrame.GetHeight())
            {
                throw new ArgumentException(
                    $"Silhouette reference for actor {actorName} has different dimensions.",
                    nameof(framesWithoutActorProductLayers));
            }
            foreach (CardSilhouetteGpuProbe probe in actor.CiProductSilhouetteGpuProbes(_camera))
            {
                Vector2 screenPosition = ScaleProjectionPoint(
                    probe.ScreenPosition,
                    projectionScale);
                bool cornerPresent = TrySamplePatch(frame, screenPosition, out Color corner);
                bool referencePresent = TrySamplePatch(
                    referenceFrame,
                    screenPosition,
                    out Color reference);
                float delta = cornerPresent && referencePresent
                    ? MathF.Max(
                        MathF.Abs(corner.R - reference.R),
                        MathF.Max(
                            MathF.Abs(corner.G - reference.G),
                            MathF.Abs(corner.B - reference.B)))
                    : 1.0f;
                bool inside = cornerPresent && referencePresent;
                probes.Add(new AnimeCardBodySilhouetteProbeEvidence
                {
                    ActorName = actor.Name,
                    Corner = probe.Corner,
                    ScreenX = float.IsFinite(screenPosition.X) ? screenPosition.X : 0.0f,
                    ScreenY = float.IsFinite(screenPosition.Y) ? screenPosition.Y : 0.0f,
                    ReferenceX = float.IsFinite(screenPosition.X)
                        ? screenPosition.X
                        : 0.0f,
                    ReferenceY = float.IsFinite(screenPosition.Y)
                        ? screenPosition.Y
                        : 0.0f,
                    FullyInsideViewport = inside,
                    CornerBackgroundColorDelta = delta,
                    Passed = inside &&
                             delta <= AnimeCardBodySilhouettePolicy.MaximumCornerBackgroundDelta,
                });
            }

            Vector2 interior = ScaleProjectionPoint(
                actor.CiProductInteriorGpuPosition(_camera),
                projectionScale);
            bool interiorInside = TryDifferencePatch(
                frame,
                referenceFrame,
                interior,
                out Rect2I roi,
                out int differencePixels);
            interiorProbes.Add(new AnimeCardBodyInteriorProbeEvidence
            {
                ActorName = actor.Name,
                ScreenX = float.IsFinite(interior.X) ? interior.X : 0.0f,
                ScreenY = float.IsFinite(interior.Y) ? interior.Y : 0.0f,
                FullyInsideViewport = interiorInside,
                RoiX = roi.Position.X,
                RoiY = roi.Position.Y,
                RoiWidth = roi.Size.X,
                RoiHeight = roi.Size.Y,
                ProductLayerDifferencePixelCount = differencePixels,
                Passed = interiorInside && differencePixels >= 4,
            });
        }

        bool basesHidden = _actors.All(actor => actor.CiProductRectangularBaseHidden);
        bool probesPass = probes.All(probe => probe.Passed);
        return new AnimeCardBodySilhouetteEvidence
        {
            State = _state,
            Required = true,
            ActorCount = _actors.Count,
            ProbeCount = probes.Count,
            InteriorProbeCount = interiorProbes.Count,
            AllRectangularBasesHidden = basesHidden,
            AllCornerProbesMatchBackground = probesPass,
            AllInteriorProbesShowProductFace = interiorProbes.All(probe => probe.Passed),
            Probes = probes,
            InteriorProbes = interiorProbes,
        };
    }

    private Vector2 ProjectionToFrameScale(Image frame)
    {
        float sourceWidth = MathF.Max(1.0f, Size.X);
        float sourceHeight = MathF.Max(1.0f, Size.Y);
        return new Vector2(
            frame.GetWidth() / sourceWidth,
            frame.GetHeight() / sourceHeight);
    }

    private static Vector2 ScaleProjectionPoint(Vector2 point, Vector2 scale) =>
        new(point.X * scale.X, point.Y * scale.Y);

    private static Rect2 ScaleProjectionRect(Rect2 rect, Vector2 scale) =>
        new(
            ScaleProjectionPoint(rect.Position, scale),
            ScaleProjectionPoint(rect.Size, scale));

    private static bool IsFiniteRect(Rect2 rect) =>
        float.IsFinite(rect.Position.X) && float.IsFinite(rect.Position.Y) &&
        float.IsFinite(rect.Size.X) && float.IsFinite(rect.Size.Y);

    private static bool IsRectFullyInside(Rect2 rect, int frameWidth, int frameHeight) =>
        IsFiniteRect(rect) && rect.Size.X > 0.0f && rect.Size.Y > 0.0f &&
        rect.Position.X >= 0.0f && rect.Position.Y >= 0.0f &&
        rect.End.X <= frameWidth && rect.End.Y <= frameHeight;

    private static bool TryDifferencePatch(
        Image frame,
        Image reference,
        Vector2 position,
        out Rect2I roi,
        out int differencePixels)
    {
        roi = new Rect2I();
        differencePixels = 0;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            return false;
        }
        int centerX = (int)MathF.Round(position.X);
        int centerY = (int)MathF.Round(position.Y);
        const int radius = 4;
        if (centerX < radius || centerY < radius ||
            centerX >= frame.GetWidth() - radius || centerY >= frame.GetHeight() - radius)
        {
            return false;
        }

        roi = new Rect2I(centerX - radius, centerY - radius, 9, 9);
        for (int y = roi.Position.Y; y < roi.End.Y; y++)
        {
            for (int x = roi.Position.X; x < roi.End.X; x++)
            {
                Color visible = frame.GetPixel(x, y);
                Color hidden = reference.GetPixel(x, y);
                float difference = MathF.Max(
                    MathF.Abs(visible.R - hidden.R),
                    MathF.Max(
                        MathF.Abs(visible.G - hidden.G),
                        MathF.Abs(visible.B - hidden.B)));
                if (difference >= 0.06f)
                {
                    differencePixels++;
                }
            }
        }
        return true;
    }

    private static bool TrySamplePatch(Image frame, Vector2 position, out Color average)
    {
        average = Colors.Transparent;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            return false;
        }
        int centerX = (int)MathF.Round(position.X);
        int centerY = (int)MathF.Round(position.Y);
        if (centerX < 1 || centerY < 1 ||
            centerX >= frame.GetWidth() - 1 || centerY >= frame.GetHeight() - 1)
        {
            return false;
        }

        Vector4 total = Vector4.Zero;
        for (int y = centerY - 1; y <= centerY + 1; y++)
        {
            for (int x = centerX - 1; x <= centerX + 1; x++)
            {
                Color pixel = frame.GetPixel(x, y);
                total += new Vector4(pixel.R, pixel.G, pixel.B, pixel.A);
            }
        }
        total /= 9.0f;
        average = new Color(total.X, total.Y, total.Z, total.W);
        return true;
    }

    private void BuildWorld()
    {
        var world = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("100c20"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("9a88bb"),
                AmbientLightEnergy = 0.72f,
            },
        };
        AddChild(world);

        _stage = new Node3D { Name = "CardBodyStage" };
        AddChild(_stage);
        var backdrop = new MeshInstance3D
        {
            Name = "MoonlitBackdrop",
            Mesh = new QuadMesh { Size = new Vector2(30.0f, 18.0f) },
            Position = new Vector3(0.0f, 0.0f, -1.2f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("181332"),
                EmissionEnabled = true,
                Emission = new Color("17112e"),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        _stage.AddChild(backdrop);

        _camera = new Camera3D
        {
            Name = "ApprovalCamera",
            Current = true,
            Fov = 44.0f,
            Position = new Vector3(0.0f, 0.0f, 17.0f),
        };
        AddChild(_camera);
        _camera.LookAt(Vector3.Zero, Vector3.Up);

        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28.0f, -20.0f, 0.0f),
            LightColor = new Color("fff0d1"),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
        };
        AddChild(light);
    }

    private void BuildOverlay()
    {
        _title = new Label
        {
            Position = new Vector2(24.0f, 18.0f),
            Size = new Vector2(720.0f, 42.0f),
            Text = "AnimeV1 一体化卡体",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        const string serifPath = "res://assets/fonts/NotoSerifCJKsc-SemiBold.otf";
        _title.AddThemeFontOverride(
            "font",
            ResourceLoader.Exists(serifPath, "Font")
                ? GD.Load<Font>(serifPath)
                : AnimeVisualTheme.DisplayFont);
        _title.AddThemeFontSizeOverride("font_size", 27);
        _title.AddThemeColorOverride("font_color", new Color("f3dfaa"));
        AddChild(_title);

        _toolbar = new HBoxContainer
        {
            Position = new Vector2(24.0f, 66.0f),
            Size = new Vector2(Size.X - 48.0f, 42.0f),
        };
        _toolbar.AddThemeConstantOverride("separation", 7);
        foreach (string state in AnimeCardBodySliceLaunch.States)
        {
            var button = new Button { Text = ShortStateName(state), CustomMinimumSize = new Vector2(116.0f, 36.0f) };
            string captured = state;
            button.Pressed += () => SetPreviewState(captured);
            AnimeVisualTheme.ApplyButton(button, AnimeFaction.Neutral);
            _toolbar.AddChild(button);
        }
        AddChild(_toolbar);
    }

    private void BuildContactSheet()
    {
        _camera.Position = new Vector3(0.0f, 0.0f, 17.0f);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
        ProductCardKind[] kinds = Enum.GetValues<ProductCardKind>();
        ProductCardFaction[] factions = Enum.GetValues<ProductCardFaction>();
        CardVisualRarity[] rarities = Enum.GetValues<CardVisualRarity>();
        for (int row = 0; row < kinds.Length; row++)
        {
            for (int factionIndex = 0; factionIndex < factions.Length; factionIndex++)
            {
                for (int rarityIndex = 0; rarityIndex < rarities.Length; rarityIndex++)
                {
                    int column = (factionIndex * rarities.Length) + rarityIndex;
                    ProductCardFaction faction = factions[factionIndex];
                    ProductCardKind kind = kinds[row];
                    AddProductActor(
                        $"Contact{row}_{column}",
                        CreateStylePreviewComposition(
                            $"{FactionLabel(faction)}·{KindLabel(kind)}",
                            kind,
                            faction,
                            rarities[rarityIndex],
                            CardFaceContext.Field,
                            cost: rarityIndex + 1,
                            attack: kind == ProductCardKind.Follower ? row + 1 : null,
                            health: kind == ProductCardKind.Follower ? row + 2 : null,
                            countdown: kind is ProductCardKind.Amulet or ProductCardKind.Trap ? 3 : null),
                        new Vector3((column - 5.5f) * 1.03f, (2.0f - row) * 1.48f, 0.0f),
                        0.42f,
                        0.0f,
                        BattlefieldCardLayout.Field);
                }
            }
        }
    }

    private void BuildRepresentatives()
    {
        _camera.Position = new Vector3(0.0f, 0.0f, 17.0f);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
        Sample[] samples = RepresentativeSamples;
        for (int index = 0; index < samples.Length; index++)
        {
            int row = index / 3;
            int column = index % 3;
            Sample sample = samples[index];
            AddSample(
                sample,
                new Vector3((column - 1.0f) * 4.0f, (1.0f - row) * 2.55f, 0.0f),
                1.02f,
                0.0f,
                CardFaceContext.Field,
                BattlefieldCardLayout.Field);
        }
    }

    private void BuildContexts()
    {
        _camera.Position = new Vector3(0.0f, 0.0f, 16.0f);
        _camera.LookAt(new Vector3(0.0f, -0.2f, 0.0f), Vector3.Up);
        Sample ace = RepresentativeSamples.First(sample =>
            sample.DesignId == "LO-11" && sample.Variant == CardFrameVariant.Normal);
        AddSample(ace, new Vector3(-4.5f, 0.5f, 0.2f), 2.05f, 0.0f, CardFaceContext.Detail, BattlefieldCardLayout.Field);
        AddSample(ace, new Vector3(-0.7f, 1.35f, 0.0f), 0.76f, 0.0f, CardFaceContext.Field, BattlefieldCardLayout.Field);
        AddSample(RepresentativeSamples[3], new Vector3(1.2f, 1.35f, 0.0f), 0.76f, 0.0f, CardFaceContext.Field, BattlefieldCardLayout.Field);
        AddSample(RepresentativeSamples[6], new Vector3(3.1f, 1.35f, 0.0f), 0.76f, 0.0f, CardFaceContext.Field, BattlefieldCardLayout.Field);
        for (int index = 0; index < 5; index++)
        {
            Sample sample = RepresentativeSamples[index % RepresentativeSamples.Length];
            AddSample(
                sample,
                new Vector3(-0.4f + (index * 1.43f), -2.3f + (MathF.Abs(index - 2) * 0.12f), 0.15f + (index * 0.015f)),
                0.92f,
                (index - 2) * -4.0f,
                CardFaceContext.Hand,
                BattlefieldCardLayout.NearHand);
        }
    }

    private void BuildHand(int count, int? hoveredIndex)
    {
        _camera.Position = new Vector3(0.0f, 0.0f, 14.0f);
        _camera.LookAt(new Vector3(0.0f, -0.55f, 0.0f), Vector3.Up);
        // The approval slice must not make the ten-card stress case smaller
        // than the product hand rig. At 1280x720, 1.22 renders roughly the
        // locked 158 px near-hand height; modest overlap keeps all ten full
        // silhouettes inside the viewport while preserving readable sockets.
        float spacing = count switch { >= 9 => 1.90f, >= 5 => 1.55f, _ => 2.2f };
        float restingScale = count >= 9 ? 1.22f : 1.16f;
        for (int index = 0; index < count; index++)
        {
            bool hovered = hoveredIndex == index;
            float distance = index - ((count - 1) * 0.5f);
            float displaced = hoveredIndex.HasValue && index != hoveredIndex.Value
                ? MathF.Sign(index - hoveredIndex.Value) * 0.34f
                : 0.0f;
            Sample sample = RepresentativeSamples[index % RepresentativeSamples.Length];
            AddSample(
                sample,
                new Vector3(
                    (distance * spacing) + displaced,
                    -1.45f + (MathF.Abs(distance) * 0.12f) + (hovered ? 0.72f : 0.0f),
                    hovered ? 0.55f : index * 0.018f),
                hovered ? 1.34f : restingScale,
                hovered ? 0.0f : Math.Clamp(distance * -0.44f, -2.0f, 2.0f),
                CardFaceContext.Hand,
                BattlefieldCardLayout.NearHand);
        }
    }

    private void BuildValueCases()
    {
        _camera.Position = new Vector3(0.0f, 0.0f, 15.0f);
        _camera.LookAt(Vector3.Zero, Vector3.Up);
        Sample[] values =
        [
            new("LO-01", "零值测试", ProductCardKind.Follower, ProductCardFaction.Oathguard, CardVisualRarity.Common, 0, 0, 0, null),
            new("LO-11", "双位数费用", ProductCardKind.Follower, ProductCardFaction.Oathguard, CardVisualRarity.Legendary, 10, 12, 14, null),
            new("AP-11", "受伤单位", ProductCardKind.Follower, ProductCardFaction.Pactmage, CardVisualRarity.Legendary, 8, 9, 3, null),
            new("LO-03", "倒数护符", ProductCardKind.Amulet, ProductCardFaction.Oathguard, CardVisualRarity.Rare, 2, null, null, 3),
            new("LO-07", "倒数伏策", ProductCardKind.Trap, ProductCardFaction.Oathguard, CardVisualRarity.Rare, 2, null, null, 10),
            new("NT-04", "无身材法术", ProductCardKind.Spell, ProductCardFaction.Neutral, CardVisualRarity.Epic, 4, null, null, null),
        ];
        for (int index = 0; index < values.Length; index++)
        {
            int row = index / 3;
            int column = index % 3;
            AddSample(values[index], new Vector3((column - 1) * 3.7f, (0.5f - row) * 3.1f, 0.0f), 1.18f, 0.0f, CardFaceContext.Hand, BattlefieldCardLayout.NearHand);
        }
    }

    private void AddSample(
        Sample sample,
        Vector3 position,
        float scale,
        float rollDegrees,
        CardFaceContext context,
        BattlefieldCardLayout layout)
    {
        AddProductActor(
            $"Card{_actors.Count}_{sample.DesignId}",
            CreateComposition(
                sample.DesignId,
                sample.Name,
                sample.Kind,
                sample.Faction,
                sample.Rarity,
                context,
                sample.Cost,
                sample.Attack,
                sample.Health,
                sample.Countdown,
                sample.Variant),
            position,
            scale,
            rollDegrees,
            layout);
    }

    private void AddProductActor(
        string nodeName,
        CardFaceComposition composition,
        Vector3 position,
        float scale,
        float rollDegrees,
        BattlefieldCardLayout layout)
    {
        var actor = new CardActor3D { Name = nodeName };
        _stage.AddChild(actor);
        Basis facing = Basis.FromEuler(new Vector3(
            Mathf.DegToRad(90.0f),
            0.0f,
            Mathf.DegToRad(rollDegrees))).Scaled(Vector3.One * scale);
        actor.BindProductFace(composition, new Transform3D(facing, position), layout);
        _actors.Add(actor);
    }

    private static CardFaceComposition CreateComposition(
        string designId,
        string name,
        ProductCardKind kind,
        ProductCardFaction faction,
        CardVisualRarity rarity,
        CardFaceContext context,
        int cost,
        int? attack,
        int? health,
        int? countdown,
        CardFrameVariant variant = CardFrameVariant.Normal)
    {
        ProductCardVisualEntry entry = ProductCardVisualCatalog.Shared.Resolve(designId);
        string artPath = ProductCardVisualCatalog.Shared.ResolveArtPath(entry, variant);
        Texture2D? art = ResourceLoader.Exists(artPath, "Texture2D")
            ? GD.Load<Texture2D>(artPath)
            : null;
        var view = new CardFaceViewModel
        {
            DesignId = designId,
            DisplayName = name,
            Kind = kind,
            Faction = faction,
            Rarity = rarity,
            Cost = cost,
            Attack = attack,
            Health = health,
            Countdown = countdown,
            Variant = variant,
            ArtPixelWidth = Math.Max(1, art?.GetWidth() ?? 1024),
            ArtPixelHeight = Math.Max(1, art?.GetHeight() ?? 1536),
            ArtFocusX = entry.ArtFocusX,
            ArtFocusY = entry.ArtFocusY,
        };
        return CardFaceComposer.Compose(
            view,
            context,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared);
    }

    private static CardFaceComposition CreateStylePreviewComposition(
        string name,
        ProductCardKind kind,
        ProductCardFaction faction,
        CardVisualRarity rarity,
        CardFaceContext context,
        int cost,
        int? attack,
        int? health,
        int? countdown)
    {
        ProductCardVisualEntry artSource =
            ProductCardVisualCatalog.Shared.Resolve(RepresentativeId(faction));
        string designId = $"STYLE-PREVIEW:{faction}:{kind}:{rarity}";
        var previewVisual = new ProductCardVisualEntry(
            designId,
            faction,
            kind,
            rarity,
            artSource.BaseArtPath,
            ArtFocusX: artSource.ArtFocusX,
            ArtFocusY: artSource.ArtFocusY);
        Texture2D? art = ResourceLoader.Exists(previewVisual.BaseArtPath, "Texture2D")
            ? GD.Load<Texture2D>(previewVisual.BaseArtPath)
            : null;
        var view = new CardFaceViewModel
        {
            DesignId = designId,
            DisplayName = name,
            Kind = kind,
            Faction = faction,
            Rarity = rarity,
            Cost = cost,
            Attack = attack,
            Health = health,
            Countdown = countdown,
            ArtPixelWidth = Math.Max(1, art?.GetWidth() ?? 1024),
            ArtPixelHeight = Math.Max(1, art?.GetHeight() ?? 1536),
            ArtFocusX = previewVisual.ArtFocusX,
            ArtFocusY = previewVisual.ArtFocusY,
        };
        return CardFaceComposer.ComposeStylePreview(
            view,
            context,
            previewVisual,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared);
    }

    private void ClearActors()
    {
        foreach (CardActor3D actor in _actors)
        {
            actor.ClearSensitive();
            _stage.RemoveChild(actor);
            actor.QueueFree();
        }
        _actors.Clear();
    }

    private async void RunCaptureSuite()
    {
        if (_captureStarted)
        {
            return;
        }
        _captureStarted = true;
        try
        {
            var suite = new AnimeCardBodySliceSuite(this, _launch.OutputDirectory!);
            string report = await suite.RunAsync();
            GD.Print($"SCGS_ANIME_CARD_BODY_SLICE_OK report={report}");
            if (_launch.ExitWhenComplete)
            {
                GetTree().Quit(0);
            }
        }
        catch (Exception exception)
        {
            GD.PrintErr($"SCGS_ANIME_CARD_BODY_SLICE_FAILED {exception}");
            GetTree().Quit(1);
        }
    }

    private static int CountNodes<T>(Node root) where T : Node
    {
        int count = root is T ? 1 : 0;
        foreach (Node child in root.GetChildren())
        {
            count += CountNodes<T>(child);
        }
        return count;
    }

    private static string RepresentativeId(ProductCardFaction faction) => faction switch
    {
        ProductCardFaction.Oathguard => "LO-11",
        ProductCardFaction.Pactmage => "AP-11",
        _ => "NT-04",
    };

    private static string FactionLabel(ProductCardFaction faction) => faction switch
    {
        ProductCardFaction.Oathguard => "曜誓",
        ProductCardFaction.Pactmage => "渊契",
        _ => "中立",
    };

    private static string KindLabel(ProductCardKind kind) => kind switch
    {
        ProductCardKind.Follower => "随从",
        ProductCardKind.Spell => "法术",
        ProductCardKind.Amulet => "护符",
        ProductCardKind.Trap => "伏策",
        ProductCardKind.Field => "场地",
        _ => "卡牌",
    };

    private static string ShortStateName(string state) => state switch
    {
        AnimeCardBodySliceLaunch.StateContact => "框体矩阵",
        AnimeCardBodySliceLaunch.StateRepresentatives => "代表卡",
        AnimeCardBodySliceLaunch.StateContexts => "三种尺寸",
        AnimeCardBodySliceLaunch.StateHandOne => "1张手牌",
        AnimeCardBodySliceLaunch.StateHandFive => "5张手牌",
        AnimeCardBodySliceLaunch.StateHandTen => "10张手牌",
        AnimeCardBodySliceLaunch.StateHandHover => "悬停",
        _ => "数值",
    };

    private static readonly Sample[] RepresentativeSamples =
    [
        new("LO-03", "晨钟誓碑", ProductCardKind.Amulet, ProductCardFaction.Oathguard, CardVisualRarity.Rare, 2, null, null, 3),
        new("LO-07", "曜誓·不破阵", ProductCardKind.Trap, ProductCardFaction.Oathguard, CardVisualRarity.Rare, 2, null, null, null),
        new("LO-11", "曜誓大团长·蕾奥妮", ProductCardKind.Follower, ProductCardFaction.Oathguard, CardVisualRarity.Legendary, 10, 8, 8, null),
        new("AP-03", "契式·违约穿刺", ProductCardKind.Spell, ProductCardFaction.Pactmage, CardVisualRarity.Rare, 2, null, null, null),
        new("AP-05", "渊契魔导院·零时讲堂", ProductCardKind.Field, ProductCardFaction.Pactmage, CardVisualRarity.Epic, 3, null, null, null),
        new("AP-11", "禁忌毕业生·诺克缇娅", ProductCardKind.Follower, ProductCardFaction.Pactmage, CardVisualRarity.Legendary, 8, 6, 6, null),
        new("NT-04", "界域裁定", ProductCardKind.Spell, ProductCardFaction.Neutral, CardVisualRarity.Epic, 4, null, null, null),
        new("LO-11", "曜誓大团长·蕾奥妮", ProductCardKind.Follower, ProductCardFaction.Oathguard, CardVisualRarity.Legendary, 10, 10, 10, null, CardFrameVariant.Evolved),
        new("AP-11", "禁忌毕业生·诺克缇娅", ProductCardKind.Follower, ProductCardFaction.Pactmage, CardVisualRarity.Legendary, 8, 8, 8, null, CardFrameVariant.Evolved),
    ];

    private sealed record Sample(
        string DesignId,
        string Name,
        ProductCardKind Kind,
        ProductCardFaction Faction,
        CardVisualRarity Rarity,
        int Cost,
        int? Attack,
        int? Health,
        int? Countdown,
        CardFrameVariant Variant = CardFrameVariant.Normal);
}

internal sealed record AnimeCardBodySliceEvidence
{
    public required string State { get; init; }
    public required int ActorCount { get; init; }
    public required int IntegratedActorCount { get; init; }
    public required int DistinctStyleCount { get; init; }
    public required string[] Contexts { get; init; }
    public required string[] DesignIds { get; init; }
    public required int SubViewportCount { get; init; }
    public required bool UsesNativeSession { get; init; }
}
