// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.CardFaces;
using Scgs.GodotClient.PresentationV2;
using Scgs.Hotseat.Product;
using Scgs.Hotseat.ProductReview;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Match;

public sealed partial class ProductMatchScreen
{
    private bool cardFrameCaptureBusy;
    // Shared with the independent static-frame performance probe.
    private bool cardFramePerformanceBusy;
    private string cardFrameCaptureResult="{\"available\":false,\"reason\":\"not_captured\"}";
    private ulong cardFrameCaptureGeneration,cardFrameCaptureRevision;
    private V05.PlayerId? cardFrameCaptureViewer;
    private CardFrameSyntheticReviewHost? cardFrameSyntheticHost;
    private EventHandler<ProductHotseatStateChangedEventArgs>? cardFrameSyntheticStateHandler;
    private ProductHotseatMatchController? cardFrameSyntheticController;
    private ulong cardFrameSyntheticGeneration,cardFrameSyntheticRevision;
    private V05.PlayerId? cardFrameSyntheticViewer;
    private bool cardFrameSyntheticPreviousInput;
    private static readonly JsonSerializerOptions FrameEvidenceJson=new(){WriteIndented=true};

    /// <summary>Real engine/window evidence, not a software-renderer performance verdict.</summary>
    public string ReviewCardFrameEnvironment()=>FrameReviewIsRevealed()
        ? JsonSerializer.Serialize(FrameEnvironment(),FrameEvidenceJson)
        : "{\"available\":false,\"reason\":\"revealed_card_frame_action_required\"}";

    public string ReviewCardFrameGlyphCaptureResult()
    {
        if(!FrameReviewIsRevealed() || cardFrameCaptureGeneration!=sessionGeneration ||
            controller!.State.Snapshot!.Revision!=cardFrameCaptureRevision ||
            controller.State.Viewer!=cardFrameCaptureViewer)
            return "{\"available\":false,\"reason\":\"capture_viewer_or_revision_not_current\"}";
        return cardFrameCaptureResult;
    }

    /// <summary>
    /// Starts a bounded GPU observation. It submits no command, reads no session,
    /// acknowledges no event and never opens a viewer gate. Poll the result method.
    /// The files contain the already revealed developer view and are not public/export assets.
    /// </summary>
    public string ReviewStartCardFrameGlyphCapture()
    {
        if(!FrameReviewIsRevealed() || cardFrameCaptureBusy || cardFramePerformanceBusy || cardFramePoolProbeBusy || cardFrameSyntheticHost is not null)
            return "{\"accepted\":false,\"reason\":\"revealed_idle_card_frame_action_required\"}";
        if(DisplayServer.GetName().Equals("headless",StringComparison.OrdinalIgnoreCase))
            return "{\"accepted\":false,\"reason\":\"display_backed_gpu_required\"}";
        string captureId=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")[..8];
        cardFrameCaptureGeneration=sessionGeneration;
        cardFrameCaptureRevision=controller!.State.Snapshot!.Revision;
        cardFrameCaptureViewer=controller.State.Viewer;
        cardFrameCaptureBusy=true;
        cardFrameCaptureResult=JsonSerializer.Serialize(new{available=true,status="capturing",capture_id=captureId});
        _=CaptureCardFrameGlyphsAsync(captureId,controller,sessionGeneration,cardFrameCaptureRevision,cardFrameCaptureViewer!.Value);
        return cardFrameCaptureResult;
    }

    /// <summary>Independent fixture viewport, never injected into the native match.</summary>
    public string ReviewShowSyntheticCardFrameLayout(string sampleKey)
    {
        if(!FrameReviewIsRevealed() || cardFrameCaptureBusy || cardFramePerformanceBusy || cardFramePoolProbeBusy)
            return "{\"accepted\":false,\"synthetic\":true,\"reason\":\"revealed_idle_card_frame_action_required\"}";
        CardFrameSyntheticSample? sample=CardFrameSyntheticSamples.All.FirstOrDefault(value=>value.Key==sampleKey);
        if(sample is null)
            return "{\"accepted\":false,\"synthetic\":true,\"reason\":\"unknown_synthetic_fixture\"}";
        try
        {
            if(cardFrameSyntheticHost is null)
            {
                cardFrameSyntheticController=controller;
                cardFrameSyntheticGeneration=sessionGeneration;
                cardFrameSyntheticRevision=controller!.State.Snapshot!.Revision;
                cardFrameSyntheticViewer=controller.State.Viewer;
                cardFrameSyntheticPreviousInput=battlefield.InputEnabled;
                battlefield.SetInputEnabled(false);
                cardFrameSyntheticHost=new CardFrameSyntheticReviewHost {Name="ExplicitSyntheticCardFrameReview"};
                cardFrameSyntheticHost.Configure(SyntheticFrameIsCurrent,CloseSyntheticCardFrameLayout);
                // Dismiss within the state-change dispatch before the next draw.
                // Synthetic data is public, but must not hide a new viewer gate.
                cardFrameSyntheticStateHandler=(_,_)=>CloseSyntheticCardFrameLayout();
                cardFrameSyntheticController!.StateChanged+=cardFrameSyntheticStateHandler;
                AddChild(cardFrameSyntheticHost);
            }
            return cardFrameSyntheticHost.Bind(sample) ? cardFrameSyntheticHost.Describe() :
                "{\"accepted\":false,\"synthetic\":true,\"reason\":\"synthetic_capture_busy_or_cancelled\"}";
        }
        catch(Exception failure)
        {
            CloseSyntheticCardFrameLayout();
            return JsonSerializer.Serialize(new{accepted=false,synthetic=true,reason=failure.Message});
        }
    }

    public string ReviewHideSyntheticCardFrameLayout()
    {
        CloseSyntheticCardFrameLayout();
        return "{\"accepted\":true,\"synthetic\":true,\"status\":\"closed\",\"commands_submitted\":0}";
    }

    public string ReviewStartSyntheticCardFrameCapture()=>cardFrameSyntheticHost?.StartCapture() ??
        "{\"accepted\":false,\"synthetic\":true,\"reason\":\"synthetic_viewport_not_open\"}";

    public string ReviewSyntheticCardFrameCaptureResult()=>cardFrameSyntheticHost?.CaptureResult() ??
        "{\"available\":false,\"synthetic\":true,\"reason\":\"synthetic_viewport_not_open\"}";

    private bool SyntheticFrameIsCurrent()=>FrameReviewIsRevealed() &&
        ReferenceEquals(controller,cardFrameSyntheticController) && sessionGeneration==cardFrameSyntheticGeneration &&
        controller!.State.Snapshot!.Revision==cardFrameSyntheticRevision && controller.State.Viewer==cardFrameSyntheticViewer;

    private void CloseSyntheticCardFrameLayout()
    {
        if(cardFrameSyntheticHost is null)return;
        bool restoreInput=SyntheticFrameIsCurrent() && cardFrameSyntheticPreviousInput;
        if(cardFrameSyntheticController is not null && cardFrameSyntheticStateHandler is not null)
            cardFrameSyntheticController.StateChanged-=cardFrameSyntheticStateHandler;
        cardFrameSyntheticStateHandler=null;
        cardFrameSyntheticHost.Close();
        cardFrameSyntheticHost=null;
        cardFrameSyntheticController=null;
        cardFrameSyntheticViewer=null;
        // A new mode/viewer controls its own input lock; never overwrite it.
        if(restoreInput)battlefield.SetInputEnabled(true);
    }

    private bool FrameReviewIsRevealed()=>CardFrameReviewRuntime.Enabled &&
        GodotObject.IsInstanceValid(this) && IsInsideTree() && !leavingScene && readyCompleted &&
        !submitting && !preparing && confirmationPurpose==ConfirmationPurpose.None &&
        controller?.State is {Mode:ProductHotseatUiMode.Action,Snapshot:not null,Viewer:not null} &&
        presentationDirector?.IsPlaying!=true && !privacy.IsVisibleInTree() &&
        !GetNode<PopupMenu>("%PauseMenu").Visible;

    private object FrameEnvironment()
    {
        string adapter=RenderingServer.GetVideoAdapterName();
        string vendor=RenderingServer.GetVideoAdapterVendor();
        string signature=(adapter+" "+vendor).ToLowerInvariant();
        bool software=new[]{"warp","basic render","llvmpipe","lavapipe","swiftshader","software"}.Any(signature.Contains);
        bool knownHardware=!software && new[]{"nvidia","geforce","radeon","amd","intel","apple"}.Any(signature.Contains);
        Vector2 viewport=GetViewport().GetVisibleRect().Size;
        return new
        {
            available=true,synthetic=false,suite="card-frame-r1-environment",
            project_absolute_path=ProjectSettings.GlobalizePath("res://"),
            project_name=ProjectSettings.GetSetting("application/config/name").AsString(),
            engine_version=Engine.GetVersionInfo()["string"].AsString(),
            adapter_name=adapter,adapter_vendor=vendor,adapter_type=RenderingServer.GetVideoAdapterType().ToString(),
            adapter_api_version=RenderingServer.GetVideoAdapterApiVersion(),
            rendering_driver=RenderingServer.GetCurrentRenderingDriverName(),
            rendering_method=RenderingServer.GetCurrentRenderingMethod(),display_server=DisplayServer.GetName(),
            software_renderer_detected=software,
            hardware_classification=software?"software":knownHardware?"reported_hardware_adapter":"unclassified",
            classification_note="Compatibility may report DeviceType.Other; renderer identity is recorded, not a benchmark pass.",
            window_size=new[]{GetWindow().Size.X,GetWindow().Size.Y},viewport_size=new[]{viewport.X,viewport.Y},
            mode=controller!.State.Mode.ToString(),viewer=controller.State.Viewer!.Value.ToString(),
            revision=controller.State.Snapshot!.Revision,
        };
    }

    private sealed record FrameLabelLease(Label3D Label,bool Visible,string Text,string Role,CardFaceRect Socket);
    private sealed record FrameActorLease(CardActor3D Actor,CardFaceComposition Face,Transform3D Pose,
        FrameLabelLease[] Labels);

    private async Task CaptureCardFrameGlyphsAsync(string captureId,ProductHotseatMatchController active,
        ulong generation,ulong revision,V05.PlayerId viewer)
    {
        var leases=new List<FrameActorLease>();
        Image? on=null,off=null;
        bool hidden=false,restored=false;
        try
        {
            RequireCurrent();
            Camera3D camera=GetViewport().GetCamera3D()??throw new InvalidOperationException("missing_camera");
            Transform3D cameraPose=camera.GlobalTransform;
            Vector2 viewport=GetViewport().GetVisibleRect().Size;
            foreach(CardActor3D actor in FrameDescendants(battlefield).OfType<CardActor3D>())
            {
                if(!actor.IsVisibleInTree() || actor.CiProductFace is not {} face ||
                    !CardFrameReviewRuntime.UsesRefinedFace(face.ViewModel.DesignId))continue;
                var labels=new List<FrameLabelLease>();
                Add("FaceLabel","name",face.Layout.NameText);
                Add("CostBadge","cost",face.Layout.CostText);
                Add("AttackBadge","attack",face.Layout.AttackText);
                Add("HealthBadge","health",face.Layout.HealthText);
                Add("CountdownBadge","countdown",face.Layout.CountdownText);
                leases.Add(new(actor,face,actor.GlobalTransform,labels.ToArray()));
                void Add(string node,string role,CardFaceRect? socket)
                {
                    if(socket is not {} box)return;
                    Label3D label=actor.GetNode<Label3D>(node);
                    labels.Add(new(label,label.Visible,label.Text,role,box));
                }
            }
            if(leases.Count is 0 or >24)throw new InvalidOperationException("visible_representative_actor_count_out_of_range");
            object environment=FrameEnvironment();
            await TwoDraws();RequireGeometry();
            on=GetViewport().GetTexture().GetImage();
            if(on.IsEmpty())throw new InvalidOperationException("empty_gpu_on_image");
            foreach(FrameActorLease lease in leases)
            {
                lease.Actor.CiSetProductValueLabelsVisible(false);
                lease.Actor.CiSetProductNameLabelVisible(false);
            }
            hidden=true;
            await TwoDraws();RequireGeometry();
            off=GetViewport().GetTexture().GetImage();
            if(off.IsEmpty() || on.GetSize()!=off.GetSize())throw new InvalidOperationException("gpu_image_size_changed");
            RestoreLabels();
            await TwoDraws();RequireGeometry();

            string directory=ProjectSettings.GlobalizePath("user://screenshots/card-frame-r1/"+captureId);
            Directory.CreateDirectory(directory);
            string onPath=Path.Combine(directory,"glyphs-on.png"),offPath=Path.Combine(directory,"glyphs-off.png");
            if(on.SavePng(onPath)!=Error.Ok || off.SavePng(offPath)!=Error.Ok)
                throw new IOException("Could not save actual GPU on/off images.");
            on.Convert(Image.Format.Rgba8);off.Convert(Image.Format.Rgba8);
            byte[] onPixels=on.GetData(),offPixels=off.GetData();
            int imageWidth=on.GetWidth(),imageHeight=on.GetHeight();
            // Camera projection uses the logical viewport (e.g. 1600x900),
            // while GetImage returns the actual render target (e.g. 1280x720).
            // Never compare a logical socket against physical image pixels.
            Vector2 logicalToImage=new(imageWidth/viewport.X,imageHeight/viewport.Y);
            var cards=new List<object>();
            foreach(FrameActorLease lease in leases)
            {
                var labels=new List<object>();
                foreach(FrameLabelLease label in lease.Labels)
                {
                    Vector2[] logicalPolygon=ProjectSocket(lease.Actor,camera,label.Socket);
                    Vector2[] polygon=logicalPolygon.Select(point=>point*logicalToImage).ToArray();
                    Rect2 socket=SocketBounds(polygon);
                    labels.Add(new
                    {
                        role=label.Role,text=label.Text,originally_visible=label.Visible,
                        logical_socket_polygon=logicalPolygon.Select(p=>new[]{p.X,p.Y}).ToArray(),
                        logical_socket_rect=FrameRect(SocketBounds(logicalPolygon)),
                        socket_space="captured_image_pixels",
                        socket_polygon=polygon.Select(p=>new[]{p.X,p.Y}).ToArray(),socket_rect=FrameRect(socket),
                        gpu_delta=MeasureGlyphDelta(onPixels,offPixels,imageWidth,imageHeight,socket,label.Visible),
                        logical_line_aabb_is_ink=false,
                    });
                }
                cards.Add(new{node=lease.Actor.GetPath().ToString(),design_id=lease.Face.ViewModel.DesignId,
                    context=lease.Face.Layout.Context.ToString(),labels});
            }
            var report=new
            {
                available=true,status="captured",capture_id=captureId,schema_version=1,
                suite="card-frame-r1-real-gpu-glyph-on-off",synthetic=false,environment,
                viewer=viewer.ToString(),revision,captured_image_size=new[]{imageWidth,imageHeight},
                logical_viewport_size=new[]{viewport.X,viewport.Y},
                logical_to_image_scale=new[]{logicalToImage.X,logicalToImage.Y},
                measurement_revision=2,
                on_image=onPath,off_image=offPath,
                on_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(onPath))).ToLowerInvariant(),
                off_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(offPath))).ToLowerInvariant(),
                completed_frame_post_draws_per_state=2,labels_restored=restored,
                session_calls=0,commands_submitted=0,event_acknowledgements=0,gate_reveals=0,
                evidence_kind="GPU difference of visible glyph layers including their outline, within projected socket plus 3px; not Label AABB or OCR",
                review_boundary="Already revealed developer-view captures; user visual approval and actual >=16px assessment remain separate.",cards,
            };
            cardFrameCaptureResult=JsonSerializer.Serialize(report,FrameEvidenceJson);
            File.WriteAllText(Path.Combine(directory,"manifest.json"),cardFrameCaptureResult);

            void RequireGeometry()
            {
                RequireCurrent();
                if(!camera.IsInsideTree() || !camera.GlobalTransform.IsEqualApprox(cameraPose) ||
                    GetViewport().GetVisibleRect().Size!=viewport)
                    throw new InvalidOperationException("camera_or_viewport_changed");
                foreach(FrameActorLease lease in leases)
                    if(!GodotObject.IsInstanceValid(lease.Actor) || !lease.Actor.IsVisibleInTree() ||
                        !ReferenceEquals(lease.Actor.CiProductFace,lease.Face) ||
                        !lease.Actor.GlobalTransform.IsEqualApprox(lease.Pose) ||
                        lease.Labels.Any(label=>!GodotObject.IsInstanceValid(label.Label) || label.Label.Text!=label.Text))
                        throw new InvalidOperationException("actor_binding_or_pose_changed");
            }
        }
        catch(Exception failure)
        {
            // The result getter also checks the original viewer/revision. Never
            // return an old private capture after a hand-off or scene restart.
            cardFrameCaptureResult=JsonSerializer.Serialize(new{available=true,status="aborted",capture_id=captureId,
                reason=failure.Message,synthetic=false});
        }
        finally
        {
            RestoreLabels();
            on?.Dispose();off?.Dispose();leases.Clear();cardFrameCaptureBusy=false;
        }

        void RequireCurrent()
        {
            if(!FrameReviewIsRevealed() || !ReferenceEquals(active,controller) || generation!=sessionGeneration ||
                active.State.Snapshot!.Revision!=revision || active.State.Viewer!=viewer)
                throw new InvalidOperationException("viewer_revision_mode_or_session_changed");
        }
        void RestoreLabels()
        {
            if(!hidden)return;
            foreach(FrameActorLease lease in leases)
            {
                // Binding equality prevents reviving labels scrubbed or rebound
                // by the real controller while an awaited frame was pending.
                if(!GodotObject.IsInstanceValid(lease.Actor) ||
                    !ReferenceEquals(lease.Actor.CiProductFace,lease.Face))continue;
                foreach(FrameLabelLease label in lease.Labels)
                    if(GodotObject.IsInstanceValid(label.Label) && label.Label.Text==label.Text)
                        label.Label.Visible=label.Visible;
            }
            hidden=false;restored=true;
        }
        async Task TwoDraws()
        {
            for(int count=0;count<2;count++)
            {
                RequireCurrent();
                Task draw=Draw();
                if(await Task.WhenAny(draw,Task.Delay(5000))!=draw)throw new TimeoutException("gpu_frame_timeout");
                await draw;RequireCurrent();
            }
        }
        async Task Draw()
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
    }

    private static Vector2[] ProjectSocket(CardActor3D actor,Camera3D camera,CardFaceRect box)
    {
        return new[]{new Vector2(box.X,box.Y),new Vector2(box.X+box.Width,box.Y),
            new Vector2(box.X+box.Width,box.Y+box.Height),new Vector2(box.X,box.Y+box.Height)}
            .Select(point=>camera.UnprojectPosition(actor.GlobalTransform*new Vector3(
                (point.X-.5f)*BattlefieldPerspective.CardWidth,CardFrameMaster.GlyphSurface,
                (point.Y-.5f)*BattlefieldPerspective.CardWidth/CardFaceLayout.CardAspectRatio))).ToArray();
    }
    private static Rect2 SocketBounds(Vector2[] polygon)
    {
        var box=new Rect2(polygon[0],Vector2.Zero);
        foreach(Vector2 point in polygon.Skip(1))box=box.Expand(point);
        return box;
    }
    private static object MeasureGlyphDelta(byte[] on,byte[] off,int width,int height,Rect2 socket,bool visible)
    {
        const int threshold=12;
        Rect2 scan=socket.Grow(3);
        int left=Math.Clamp((int)MathF.Floor(scan.Position.X),0,width),top=Math.Clamp((int)MathF.Floor(scan.Position.Y),0,height);
        int right=Math.Clamp((int)MathF.Ceiling(scan.End.X),0,width),bottom=Math.Clamp((int)MathF.Ceiling(scan.End.Y),0,height);
        int minX=right,minY=bottom,maxX=-1,maxY=-1,count=0;
        for(int y=top;y<bottom;y++)for(int x=left;x<right;x++)
        {
            int at=(y*width+x)*4;
            if(Math.Max(Math.Abs(on[at]-off[at]),Math.Max(Math.Abs(on[at+1]-off[at+1]),Math.Abs(on[at+2]-off[at+2])))<threshold)continue;
            count++;minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);
        }
        bool found=visible && count>0;
        return new{threshold_8bit=threshold,changed_pixels=count,visible_pixels_found=found,
            ink_bounds=found?new[]{minX,minY,maxX-minX+1,maxY-minY+1}:Array.Empty<int>(),
            ink_height_pixels=found?maxY-minY+1:0,scan_rect=new[]{left,top,right-left,bottom-top},
            note="Raw GPU on/off difference; overlapping cards or other motion require human rejection, not an automatic visual pass."};
    }
    private static float[] FrameRect(Rect2 box)=>[box.Position.X,box.Position.Y,box.Size.X,box.Size.Y];
    private static IEnumerable<Node> FrameDescendants(Node node)
    {
        foreach(Node child in node.GetChildren())
        {
            yield return child;
            foreach(Node descendant in FrameDescendants(child))yield return descendant;
        }
    }
}
