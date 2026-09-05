"""R1 source/actual-GLB contracts; not Godot/GPU or visual acceptance.

These checks lock the candidate-only projection/side-lane boundary and inspect
the model's decoded, transformed POSITION bytes. They deliberately do not infer
pixel glyph height, HUD occlusion or native gameplay success from source text.
"""
# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import re
import struct
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from check_card_frame_r1_assets import audit_model  # noqa: E402

BATTLEFIELD = ROOT / "client/godot/scripts/Battlefield"


def source(name: str) -> str:
    return (BATTLEFIELD / name).read_text(encoding="utf-8")


def uncomment(value: str) -> str:
    return re.sub(r"//[^\n]*|/\*.*?\*/", "", value, flags=re.S)


def compact(value: str) -> str:
    return re.sub(r"\s+", "", uncomment(value))


def body(value: str, signature: str) -> str:
    start = value.index("{", value.index(signature) + len(signature))
    depth = 1
    cursor = start + 1
    while depth and cursor < len(value):
        depth += (value[cursor] == "{") - (value[cursor] == "}")
        cursor += 1
    if depth:
        raise AssertionError(f"Unclosed source block: {signature}")
    return value[start + 1:cursor - 1]


def f32(number: float) -> float:
    return struct.unpack("<f", struct.pack("<f", number))[0]


class CardFrameProjectionContracts(unittest.TestCase):
    def test_default_perspective_constants_are_preserved(self) -> None:
        text = source("BattlefieldPerspective.cs")
        expected = {
            "BoardWidth": 19.8, "BoardDepth": 16.6,
            "CardWidth": 1.58, "CardDepth": 2.18,
            "SlotWidth": 1.88, "SlotDepth": 2.48,
            "TerritoryBoundaryClearance": .12,
            "CameraFovDegrees": 58, "CameraPitchDegrees": 58,
            "MinimumZoom": .82, "MaximumZoom": 1.24,
            "UnitSpacing": 2.4, "TacticSpacing": 3.15,
            "UnitRowDepth": 1.55, "TacticRowDepth": 4.10,
            "SideZoneX": 7.1, "ZonePileScale": .82,
            "DeckDepth": 1.25, "GraveyardDepth": 3.45,
            "ArchiveDepth": 5.65, "StandbyDepth": 1.45,
            "CornerZoneDepth": 5.35,
        }
        for name, number in expected.items():
            with self.subTest(constant=name):
                match = re.search(rf"const float {name}\s*=\s*([\d.]+)f\s*;", text)
                self.assertIsNotNone(match)
                self.assertEqual(float(match.group(1)), number)

    def test_main_five_and_three_tactic_positions_do_not_use_review_geometry(self) -> None:
        text = source("BattlefieldPerspective.cs")
        for method, count, spacing, depth, y in (
            ("UnitTransform", "UnitSlotCount", "UnitSpacing", "UnitRowDepth", ".22"),
            ("TacticTransform", "TacticSlotCount", "TacticSpacing", "TacticRowDepth", ".18"),
        ):
            with self.subTest(method=method):
                actual = compact(body(text, "public static Transform3D " + method + "("))
                expected = compact(f"""
                    int visual = VisualSlotIndex(player, viewer, slot, {count});
                    float x = (visual - (({count} - 1) / 2.0f)) * {spacing};
                    float z = IsNear(player, viewer) ? {depth} : -{depth};
                    return CreateFlatTransform(player, viewer, new Vector3(x, 0{y}f, z));
                """)
                self.assertEqual(actual, expected)

    def test_side_lane_and_leader_reposition_are_strictly_candidate_only(self) -> None:
        text = compact(source("BattlefieldPerspective.cs"))
        self.assertIn("privatestaticfloatSideLaneX=>Scgs.GodotClient.PresentationV2."
                      "CardFrameReviewRuntime.Enabled?6.45f:SideZoneX;", text)
        leader = compact(body(source("BattlefieldPerspective.cs"), "public static Transform3D LeaderTransform("))
        self.assertEqual(leader, compact("""
            float z = IsNear(player, viewer) ? CornerZoneDepth : -CornerZoneDepth;
            float leaderX = SideLaneX + .05f;
            if (Scgs.GodotClient.PresentationV2.CardFrameReviewRuntime.Enabled)
                leaderX = IsNear(player, viewer) ? 7.0f : 6.7f;
            float x = IsNear(player, viewer) ? -leaderX : leaderX;
            return CreateFlatTransform(player, viewer, new Vector3(x, 0.26f, z));
        """))
        # Also check C# float rounding: the false branch preserves the former
        # literal 7.15f, not merely approximate Python decimal arithmetic.
        self.assertEqual(f32(f32(7.1) + f32(.05)), f32(7.15))
        for method in ("ProductFieldTransform", "StandbyTransform", "StandbyPileTransform", "PileTransform"):
            self.assertIn("SideLaneX", body(source("BattlefieldPerspective.cs"), "public static Transform3D " + method + "("))

    def test_orthographic_projection_is_not_enabled_by_default_or_ordinary_product(self) -> None:
        camera = source("BattlefieldCameraRig.cs")
        self.assertIn("private bool _cardFrameReview;", camera)
        self.assertNotIn("SetCardFrameReviewFraming", body(camera, "public override void _Ready()"))
        setting = body(camera, "public void SetCardFrameReviewFraming()")
        self.assertIn("_cardFrameReview = true;", setting)
        self.assertEqual(uncomment(camera).count("_cardFrameReview = true;"), 1)
        pose = body(camera, "private void ApplyPose()")
        candidate = body(pose, "if (_cardFrameReview)")
        self.assertIn("Projection = ProjectionType.Orthogonal;", candidate)
        self.assertNotIn("Projection =", pose.replace(candidate, ""))
        product = body(source("Battlefield3DPresenter.Product.cs"), "internal void ConfigureProductPresentation()")
        self.assertIn("if(Scgs.GodotClient.PresentationV2.CardFrameReviewRuntime.Enabled &&", product)
        # The `is {}` property pattern is part of the condition, not its body.
        guarded = body(product, 'GetNodeOrNull<WorldEnvironment>("Environment") is {} env)')
        self.assertIn("_camera.SetCardFrameReviewFraming();", guarded)
        self.assertNotIn("SetCardFrameReviewFraming", product.replace(guarded, ""))

    def test_review_orthographic_size_and_zoom_range_are_bounded(self) -> None:
        camera = source("BattlefieldCameraRig.cs")
        candidate = compact(body(body(camera, "private void ApplyPose()"), "if (_cardFrameReview)"))
        self.assertIn("Mathf.InverseLerp(BattlefieldPerspective.MinimumZoom,BattlefieldPerspective.MaximumZoom,_zoom)", candidate)
        self.assertIn("Size=Mathf.Lerp(12.7f,13.0f,wheelFraction)*MathF.Max(1.0f,MinimumProductAspectRatio/aspectRatio);", candidate)
        self.assertIn("Mathf.Clamp(zoom,BattlefieldPerspective.MinimumZoom,BattlefieldPerspective.MaximumZoom)",
                      compact(body(camera, "public bool SetZoom(float zoom)")))
        # Product 16:9..16:10 does not silently widen the candidate lens beyond
        # 13. Narrow diagnostic viewports retain an explicitly different scope.
        for aspect in (16 / 9, 16 / 10):
            for fraction in (0, .25, .5, .75, 1):
                size = (12.7 + .3 * fraction) * max(1, 1.6 / aspect)
                self.assertGreaterEqual(size, 12.7)
                self.assertLessEqual(size, 13)

    def test_hand_orthographic_scale_uses_real_lens_projection_not_fov_guess(self) -> None:
        hand = body(source("BattlefieldHandRig.cs"), "private Transform3D CreateCameraFacingTransform(")
        ortho = compact(body(hand, "if (_camera.Projection == Camera3D.ProjectionType.Orthogonal)"))
        self.assertIn("worldHeight=_camera.ProjectPosition(newVector2(0,viewportHeight),cameraDepth)"
                      ".DistanceTo(_camera.ProjectPosition(Vector2.Zero,cameraDepth));", ortho)
        self.assertNotIn("Fov", ortho)
        self.assertIn("Vector3 origin = _camera.ProjectPosition(screenCenter, cameraDepth);", hand)
        self.assertIn("float scale = pixelHeight * worldHeight /", hand)
        # The pre-existing perspective path is intentionally preserved.
        self.assertIn("2.0f * cameraDepth", hand)
        self.assertIn("MathF.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f)", hand)
        raycast = source("BattlefieldRaycastInput.cs")
        self.assertIn("ProjectRayOrigin(screenPosition)", raycast)
        self.assertIn("ProjectRayNormal(screenPosition)", raycast)

    def test_public_back_compaction_is_opt_in_and_not_a_hand_profile_change(self) -> None:
        hand = source("BattlefieldHandRig.cs")
        pose_start = hand.index("public HandCardPose CreatePose(")
        signature = compact(hand[pose_start:hand.index("{", pose_start)])
        self.assertTrue(signature.endswith("boolcompactPublicBacks=false)"))
        pose = compact(body(hand, "public HandCardPose CreatePose("))
        reduction = "if(compactPublicBacks&&near)basePixelHeight*=.5f;"
        self.assertEqual(pose.count(reduction), 1)
        # No other branch, identity, profile mutation, or camera-depth override
        # may be keyed to this flag: it only halves the anonymous near fan.
        self.assertNotIn("compactPublicBacks", pose.replace(reduction, ""))
        self.assertEqual(compact(hand).count("compactPublicBacks"), 2)
        self.assertIn("boolnear=BattlefieldPerspective.IsNear(player,viewer);", pose)
        self.assertIn("floatpixelHeight=basePixelHeight*focusScale;", pose)
        self.assertNotRegex(pose, r"hand\.\w+\s*=(?!=)")

    def test_public_back_compaction_call_is_only_r1_public_projection(self) -> None:
        presenter = source("Battlefield3DPresenter.cs")
        relayout = compact(body(presenter, "private void RelayoutHands(bool animate)"))
        guarded_call = (
            "_handRig.CreatePose(binding.Player,PerspectiveViewer,binding.Index,"
            "binding.Count,hoveredIndex,selectedIndex,compactPublicBacks:"
            "Scgs.GodotClient.PresentationV2.CardFrameReviewRuntime.Enabled&&!_privateRender)"
        )
        self.assertIn(guarded_call, relayout)
        # Existing private/legacy callers omit the optional argument. Lock the
        # sole explicit opt-in so a later call cannot silently shrink faces.
        named_callers = []
        for path in (ROOT / "client/godot/scripts").rglob("*.cs"):
            occurrences = compact(path.read_text(encoding="utf-8")).count("compactPublicBacks:")
            named_callers.extend([path.relative_to(ROOT).as_posix()] * occurrences)
        self.assertEqual(named_callers, [
            "client/godot/scripts/Battlefield/Battlefield3DPresenter.cs"
        ])

    def test_public_back_compaction_boundary_truth_table(self) -> None:
        # Source-contracted Boolean boundary, not a C#/Godot execution test or
        # GPU claim. Root's real Presenting screenshot is separate evidence.
        for candidate in (False, True):
            for private_render in (False, True):
                for near in (False, True):
                    with self.subTest(candidate=candidate, private=private_render, near=near):
                        compact_public_backs = candidate and not private_render
                        factor = .5 if compact_public_backs and near else 1
                        self.assertEqual(
                            factor,
                            .5 if (candidate, private_render, near) == (True, False, True) else 1,
                        )
                        if not candidate or private_render or not near:
                            self.assertEqual(factor, 1, "Default/private/far hands must be unchanged")

    def test_candidate_whole_card_scale_is_guarded_and_real_glb_fits_the_same_slot(self) -> None:
        actor = compact(body(source("CardActor3D.cs"), "internal void BindProductFace("))
        self.assertIn("if(!reviewPoseAlreadyScaled&&composition.Layout.Context==CardFaceContext.Field&&"
                      "CardFrameReviewRuntime.UsesRefinedFace(composition.ViewModel.DesignId))"
                      "transform=transform.ScaledLocal(Vector3.One*1.16f);", actor)
        for name, limit in (("frame-master.glb", 35000), ("frame-master-low.glb", 14000)):
            with self.subTest(lod=name):
                evidence = audit_model(ROOT / "client/godot/assets/visual/anime_v1/card_frame_r1" / name, limit)
                lo, hi = evidence["minimum"], evidence["maximum"]
                # These extents are decoded from POSITION bytes with actual
                # GLB parent matrices, not accessor min/max or a second box.
                for axis, slot in ((0, 1.88), (2, 2.48)):
                    self.assertGreaterEqual(lo[axis] * 1.16, -slot / 2)
                    self.assertLessEqual(hi[axis] * 1.16, slot / 2)
                self.assertLessEqual((hi[0] - lo[0]) * 1.16, 1.88)
                self.assertLessEqual((hi[2] - lo[2]) * 1.16, 2.48)

    def test_leader_furniture_scale_does_not_scale_main_card_actors(self) -> None:
        leader = compact(body(source("Battlefield3DPresenter.cs"), "private void RenderLeader("))
        self.assertIn("if(Scgs.GodotClient.PresentationV2.CardFrameReviewRuntime.Enabled)"
                      "actor.SetPresentationScale(.65f);", leader)
        self.assertIn("SlotActor3Dactor=RentSlot();", leader)
        self.assertNotIn("CardActor3D", leader)


if __name__ == "__main__":
    unittest.main()
