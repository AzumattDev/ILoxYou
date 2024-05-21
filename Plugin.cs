﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace ILoxYou
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class ILoxYouPlugin : BaseUnityPlugin
    {
        internal const string ModName = "ILoxYou";
        internal const string ModVersion = "1.0.1";
        internal const string Author = "Azumatt";
        private const string ModGUID = $"{Author}.{ModName}";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource ILoxYouLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        public void Awake()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StartDoodadControl))]
    static class PlayerStartDoodadControlPatch
    {
        public static bool RidingLox;
        public static Humanoid RidingHumanoid = null!;

        static void Postfix(Player __instance, IDoodadController shipControl)
        {
#if DEBUG
            ILoxYouPlugin.ILoxYouLogger.LogDebug($"PlayerIsRidingPatch: They are on {Utils.GetPrefabName(__instance.m_doodadController.GetControlledComponent().gameObject.name)}");
#endif
            if (Utils.GetPrefabName(shipControl.GetControlledComponent().gameObject.name) == "Lox")
            {
                RidingLox = true;
                RidingHumanoid = shipControl.GetControlledComponent().transform.GetComponentInParent<Humanoid>();
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StopDoodadControl))]
    static class PlayerStopDoodadControlPatch
    {
        static bool Prefix(Player __instance)
        {
            if (__instance.m_doodadController == null || !__instance.m_doodadController.IsValid())
            {
                // Ensure dismount if the mount dies
                PlayerStartDoodadControlPatch.RidingLox = false;
                PlayerStartDoodadControlPatch.RidingHumanoid = null!;
                return true;
            }


            return !PlayerStartDoodadControlPatch.RidingLox;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    static class HumanoidStartAttackPatch
    {
        static bool Prefix(Humanoid __instance)
        {
            if (__instance != Player.m_localPlayer) return true;
            return !PlayerStartDoodadControlPatch.RidingLox;
        }
    }


    [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
    static class PlayerAttachStopPatch
    {
        static bool Prefix(Player __instance)
        {
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
                __instance.CustomAttachStop();
                return;
            }
                
            // Detect and handle jump input specifically for dismounting
            if (ZInput.GetButton("Jump") || ZInput.GetButtonDown("JoyJump"))
            {
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
            p.m_doodadController = null;
            p.StopDoodadControl();
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

    public static class KeyboardExtensions
    {
        public static bool IsKeyDown(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }

        public static bool IsKeyHeld(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }
    }
}