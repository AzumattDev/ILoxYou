﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ILoxYou
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class ILoxYouPlugin : BaseUnityPlugin
    {
        internal const string ModName = "ILoxYou";
        internal const string ModVersion = "1.0.8";
        internal const string Author = "Azumatt";
        private const string ModGUID = $"{Author}.{ModName}";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource ILoxYouLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        public static Sprite? LoxSprite = null;
        public const string Fab = "TrophyLox";

        public void Awake()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
        }

        public static void LogIfDebug(string message)
        {
#if DEBUG
            ILoxYouLogger.LogDebug(message);
#endif
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    static class ObjectDBAwakePatch
    {
        static void Postfix(ObjectDB __instance)
        {
            if (__instance.GetItemPrefab(ILoxYouPlugin.Fab) == null)
                return;

            ILoxYouPlugin.LoxSprite = __instance.GetItemPrefab(ILoxYouPlugin.Fab).GetComponent<ItemDrop>().m_itemData.m_shared.m_icons[0];
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateEventPin))]
    public static class UpdateEventPinPatch
    {
        public static void Postfix(Minimap __instance)
        {
            if (PlayerStartDoodadControlPatch.LastHumanoidZDOID.IsNone()) return;
            //Populate the list of current HUD characters.
            List<Character> guysList =
                (from hud
                        in EnemyHud.instance.m_huds.Values
                    where hud.m_character != null
                          && hud.m_character.IsTamed()
                          && hud.m_character.GetZDOID() == PlayerStartDoodadControlPatch.LastHumanoidZDOID
                    select hud.m_character
                ).ToList();
            //Add minimap pins if they haven't been added already.
            foreach (Character character
                     in from character in guysList
                     where character is not Player
                     let flag = __instance.m_pins.Any(pin => pin.m_name.Equals($"$hud_tame {character.GetHoverName()} [Health: {character.GetHealth()}]"))
                     where !flag
                     select character)
            {
                Minimap.PinData? pin = __instance.AddPin(character.GetCenterPoint(), Minimap.PinType.None, $"$hud_tame {character.GetHoverName()} [Health: {character.GetHealth()}]", false, false);
                if (ILoxYouPlugin.LoxSprite != null)
                    pin.m_icon = ILoxYouPlugin.LoxSprite;
                Sadle? sadle = null;
                EnemyHud.instance.UpdateHuds(Player.m_localPlayer, sadle, Time.deltaTime);
            }

            //Remove minimap pins which are not needed anymore.
            List<Minimap.PinData> removePins = new();

            foreach (Minimap.PinData pin in __instance.m_pins)
            {
                if (pin.m_type != Minimap.PinType.None) continue;
                bool flag = false;
                foreach (Character character in guysList.Where(character => pin.m_name.Equals($"$hud_tame {character.GetHoverName()} [Health: {character.GetHealth()}]")))
                {
                    pin.m_pos.x = character.GetCenterPoint().x;
                    pin.m_pos.y = character.GetCenterPoint().y;
                    pin.m_pos.z = character.GetCenterPoint().z;
                    flag = true;
                    break;
                }

                if (!flag)
                {
                    if (pin.m_icon.Equals(ILoxYouPlugin.LoxSprite))
                    {
                        ILoxYouPlugin.ILoxYouLogger.LogDebug("pin to remove: " + pin.m_name + "::" + pin.m_icon + "::" + pin.m_iconElement);
                        removePins.Add(pin);
                    }
                }
            }

            if (removePins.Count > 0)
            {
                ILoxYouPlugin.ILoxYouLogger.LogDebug("number of pins to remove: " + removePins.Count);
            }

            foreach (Minimap.PinData pin in removePins)
            {
                __instance.RemovePin(pin);
                ILoxYouPlugin.ILoxYouLogger.LogDebug("removing pin for " + pin.m_name);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StartDoodadControl))]
    static class PlayerStartDoodadControlPatch
    {
        public static bool RidingLox;
        public static Humanoid RidingHumanoid = null!;
        public static ZDOID LastHumanoidZDOID;

        static void Postfix(Player __instance, IDoodadController shipControl)
        {
            ILoxYouPlugin.LogIfDebug($"PlayerIsRidingPatch: They are on {Utils.GetPrefabName(__instance.m_doodadController.GetControlledComponent().gameObject.name)}");
            if (Utils.GetPrefabName(shipControl.GetControlledComponent().gameObject.name) == "Lox")
            {
                RidingLox = true;
                RidingHumanoid = shipControl.GetControlledComponent().transform.GetComponentInParent<Humanoid>();
                LastHumanoidZDOID = RidingHumanoid.GetZDOID();
                ILoxYouPlugin.LogIfDebug($"Player is riding a Lox. Humanoid ZDOID: {LastHumanoidZDOID}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StopDoodadControl))]
    static class PlayerStopDoodadControlPatch
    {
        static bool Prefix(Player __instance)
        {
            ILoxYouPlugin.LogIfDebug("PlayerStopDoodadControlPatch: Attempting to stop doodad control.");
            if (__instance.m_doodadController == null || !__instance.m_doodadController.IsValid())
            {
                // Ensure dismount if the mount dies
                ILoxYouPlugin.LogIfDebug("PlayerStopDoodadControlPatch: Doodad controller is invalid or null.");
                PlayerStartDoodadControlPatch.RidingLox = false;
                PlayerStartDoodadControlPatch.RidingHumanoid = null!;
                return true;
            }


            if (PlayerStartDoodadControlPatch.RidingLox) return false;
            ILoxYouPlugin.LogIfDebug("PlayerStopDoodadControlPatch: Player is not riding a Lox.");
            return true;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    static class HumanoidStartAttackPatch
    {
        static bool Prefix(Humanoid __instance)
        {
            ILoxYouPlugin.LogIfDebug($"HumanoidStartAttackPatch: Humanoid {__instance.GetHoverName()} attempting to start attack.");
            if (__instance != Player.m_localPlayer) return true;
            ILoxYouPlugin.LogIfDebug($"HumanoidStartAttackPatch: Player is {Player.m_localPlayer.GetHoverName()} and riding Lox: {PlayerStartDoodadControlPatch.RidingLox}");
            return !PlayerStartDoodadControlPatch.RidingLox || Player.m_localPlayer.m_doodadController == null;
        }
    }


    [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
    static class PlayerAttachStopPatch
    {
        static bool Prefix(Player __instance)
        {
            ILoxYouPlugin.LogIfDebug($"PlayerAttachStopPatch: Player is attempting to attach stop. Riding Lox: {PlayerStartDoodadControlPatch.RidingLox}");
            return !PlayerStartDoodadControlPatch.RidingLox;
        }
    }


    [HarmonyPatch(typeof(Player), nameof(Player.UpdateDoodadControls))]
    static class PlayerUpdateDoodadControlsPatch
    {
        static void Postfix(Player __instance)
        {
            if (__instance.m_doodadController == null || !__instance.m_doodadController.IsValid() || !PlayerStartDoodadControlPatch.RidingLox)
                return;

            // Check if the mount is dead
            if (PlayerStartDoodadControlPatch.RidingHumanoid?.GetHealth() <= 0)
            {
                ILoxYouPlugin.LogIfDebug("PlayerUpdateDoodadControlsPatch: Mount is dead, stopping control.");

                __instance.CustomAttachStop();
                return;
            }

            // Detect and handle jump/interact input specifically for dismounting
            if (ZInput.GetButton("Jump") || ZInput.GetButtonDown("JoyJump") || ((ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")) && !Hud.InRadial() && __instance.m_hovering?.GetComponent<Sadle>() == null))
            {
                ILoxYouPlugin.LogIfDebug("PlayerUpdateDoodadControlsPatch: Jump button pressed, stopping control.");
                __instance.CustomAttachStop();
                return;
            }

            __instance.HandleInput();
        }
    }

    public static class PlayerExtensions
    {
        public static void CustomAttachStop(this Player p)
        {
            if (p.m_sleeping || !p.m_attached)
                return;
            if (p.m_attachPoint != null)
                p.transform.position = p.m_attachPoint.TransformPoint(p.m_detachOffset);
            if (p.m_attachColliders != null)
            {
                foreach (Collider attachCollider in p.m_attachColliders)
                {
                    if (attachCollider)
                        Physics.IgnoreCollision(p.m_collider, attachCollider, false);
                }

                p.m_attachColliders = null;
            }

            p.m_body.useGravity = true;
            p.m_attached = false;
            p.m_attachPoint = null;
            p.m_attachPointCamera = null;
            p.m_zanim.SetBool(p.m_attachAnimation, false);
            p.m_nview.GetZDO().Set(ZDOVars.s_inBed, false);
            p.ResetCloth();
            PlayerStartDoodadControlPatch.RidingLox = false; //must be set to false before StopDoodadControl or the Prefix patch will cause the original function to not fire.
            p.StopDoodadControl();
            PlayerStartDoodadControlPatch.RidingHumanoid = null!;
        }

        public static void HandleInput(this Player player)
        {
            void ProcessInput(KeyCode key, int weaponIndex, string controllerButton = "")
            {
                bool buttonDown = false;
                // Check for Gamepad input not keyboard
                buttonDown = ZInput.IsGamepadActive() ? ZInput.GetButtonDown(controllerButton) : Input.GetKeyDown(key);
                if (!PlayerStartDoodadControlPatch.RidingHumanoid || !buttonDown || Menu.IsVisible() || !player.TakeInput()) return;
                if (PlayerStartDoodadControlPatch.RidingHumanoid.InAttack())
                    return;

                List<ItemDrop.ItemData> items = PlayerStartDoodadControlPatch.RidingHumanoid.m_inventory.GetAllItems().Where(i => i.IsWeapon()).ToList();

                if (items.Count <= weaponIndex) return;
                ItemDrop.ItemData weapon = items[weaponIndex];
                PlayerStartDoodadControlPatch.RidingHumanoid.EquipItem(weapon);
                float stamtoUse = weapon.m_shared.m_attack.m_attackStamina;
                Sadle? doodadController = player.GetDoodadController() as Sadle;
                if (doodadController == null) return;
                if (weapon.m_shared.m_attack.m_attackStamina <= 0f)
                {
                    stamtoUse = doodadController.GetMaxStamina() * 0.05f;
                }

                if (!doodadController.HaveStamina(stamtoUse)) return;
                doodadController.UseStamina(stamtoUse);
                PlayerStartDoodadControlPatch.RidingHumanoid.StartAttack(null, false);
            }

            ProcessInput(KeyCode.Mouse0, 0, "JoyAttack"); // Left click or Right Bumper
            ProcessInput(KeyCode.Mouse1, 1, "JoyBlock"); // Right click or Left Bumper
            ProcessInput(KeyCode.Mouse2, 2, "JoySecondaryAttack"); // Middle click or Right Trigger
        }
    }
}