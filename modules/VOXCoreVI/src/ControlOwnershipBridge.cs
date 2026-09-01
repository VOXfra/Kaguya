using GTA;
using System;
using System.Collections.Generic;

namespace VOX.CoreVI
{
    public sealed class ControlClaimSnapshot
    {
        public int Control;
        public string Owner = string.Empty;
        public int Priority;
        public int ExpiresAt;
        public string Context = string.Empty;
    }

    public static class ControlOwnershipBridge
    {
        private sealed class Claim
        {
            public int Control;
            public string Owner = string.Empty;
            public int Priority;
            public int ExpiresAt;
            public string Context = string.Empty;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, Claim> Claims = new Dictionary<int, Claim>();

        // Priority guide used by the VOX modules:
        // 10 passive affordance, 30 contextual UI, 50 active physical interaction,
        // 70 safety-critical temporary lock, 100 reserved for Rockstar/mission yield.
        public static bool TryClaim(string owner, int control, int priority, int ttlMs, string context)
        {
            if (string.IsNullOrWhiteSpace(owner) || control < 0) return false;
            int now = Game.GameTime;
            int expires = now + Math.Max(30, Math.Min(5000, ttlMs));
            lock (Sync)
            {
                CleanupInternal(now);
                Claim existing;
                if (Claims.TryGetValue(control, out existing))
                {
                    if (string.Equals(existing.Owner, owner, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.Priority = Math.Max(existing.Priority, priority);
                        existing.ExpiresAt = expires;
                        existing.Context = context ?? string.Empty;
                        return true;
                    }
                    if (priority <= existing.Priority) return false;
                }

                Claims[control] = new Claim
                {
                    Control = control,
                    Owner = owner,
                    Priority = priority,
                    ExpiresAt = expires,
                    Context = context ?? string.Empty
                };
                return true;
            }
        }

        public static bool Owns(string owner, int control)
        {
            if (string.IsNullOrWhiteSpace(owner) || control < 0) return false;
            int now = Game.GameTime;
            lock (Sync)
            {
                CleanupInternal(now);
                Claim claim;
                return Claims.TryGetValue(control, out claim) && string.Equals(claim.Owner, owner, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string CurrentOwner(int control)
        {
            int now = Game.GameTime;
            lock (Sync)
            {
                CleanupInternal(now);
                Claim claim;
                return Claims.TryGetValue(control, out claim) ? claim.Owner : string.Empty;
            }
        }

        public static void Release(string owner, int control)
        {
            if (string.IsNullOrWhiteSpace(owner)) return;
            lock (Sync)
            {
                if (control >= 0)
                {
                    Claim claim;
                    if (Claims.TryGetValue(control, out claim) && string.Equals(claim.Owner, owner, StringComparison.OrdinalIgnoreCase))
                        Claims.Remove(control);
                    return;
                }

                var remove = new List<int>();
                foreach (var pair in Claims)
                    if (string.Equals(pair.Value.Owner, owner, StringComparison.OrdinalIgnoreCase)) remove.Add(pair.Key);
                foreach (int key in remove) Claims.Remove(key);
            }
        }

        public static ControlClaimSnapshot[] Snapshot()
        {
            int now = Game.GameTime;
            lock (Sync)
            {
                CleanupInternal(now);
                var result = new List<ControlClaimSnapshot>();
                foreach (Claim claim in Claims.Values)
                {
                    result.Add(new ControlClaimSnapshot
                    {
                        Control = claim.Control,
                        Owner = claim.Owner,
                        Priority = claim.Priority,
                        ExpiresAt = claim.ExpiresAt,
                        Context = claim.Context
                    });
                }
                return result.ToArray();
            }
        }

        internal static void Cleanup()
        {
            lock (Sync) CleanupInternal(Game.GameTime);
        }

        internal static void ClearAll()
        {
            lock (Sync) Claims.Clear();
        }

        private static void CleanupInternal(int now)
        {
            if (Claims.Count == 0) return;
            var remove = new List<int>();
            foreach (var pair in Claims)
                if (pair.Value == null || now >= pair.Value.ExpiresAt) remove.Add(pair.Key);
            foreach (int key in remove) Claims.Remove(key);
        }
    }
}
