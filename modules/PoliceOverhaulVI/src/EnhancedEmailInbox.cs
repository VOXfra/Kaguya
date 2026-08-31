using GTA;
using GTA.Native;
using System;

namespace VOX.PoliceOverhaulVI
{
    // Guarded Story Mode email-inbox bridge for GTA V Enhanced 1.73 / b1158.13.
    // The numbered globals are build-specific. If the running Online version is
    // not explicitly supported, this class performs no writes and callers keep
    // the ordinary feed notification + disk archive fallback.
    internal static class EnhancedEmailInbox
    {
        private const string SupportedOnlineVersion = "1.73";

        // Enhanced 1.73 / b1158.13 appEmail layout.
        private const int PhoneOwnerIndex = 21666;
        private const int EmailData = 46183;
        private const int EmailCampaigns = 49508;
        private const int EmailInbox = 55121;
        private const int EmailDefStride = 12;
        private const int EmailCampaignStride = 46;
        private const int EmailInboxStride = 120;

        // Reserved custom slots. One persistent LSPD history message is updated
        // rather than allocating arbitrary campaign slots inside Rockstar data.
        private const int DefinitionSlot = 180;
        private const int CampaignSlot = 110;
        private const int HeaderStringId = 900;
        private const int BodyStringId = 901;
        private const int InboxSlots = 16;

        // Los Santos Tourist Info is used only as the underlying vanilla sender
        // record because appEmail requires a native sender id. The subject/body
        // themselves identify the message as LSPD Traffic Division.
        private const int NativeSenderId = 19;

        private static bool _unsupportedLogged;

        public static bool UpsertTrafficHistory(string subject, string body, Action<string> log)
        {
            try
            {
                string online = Function.Call<string>(Hash.GET_ONLINE_VERSION) ?? string.Empty;
                if (!string.Equals(online.Trim(), SupportedOnlineVersion, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_unsupportedLogged && log != null)
                    {
                        _unsupportedLogged = true;
                        log("Persistent iFruit inbox disabled: unsupported Online layout '" + online + "'. Feed notification/archive fallback remains active.");
                    }
                    return false;
                }

                int owner = GlobalVariable.Get(PhoneOwnerIndex).Read<int>();
                if (owner < 0 || owner > 4) return false;

                // appEmail resolves these integer string ids as EMSTR_<id>.
                // Registering them dynamically lets the persistent inbox row use
                // current fine-history text without replacing any vanilla GXT file.
                Function.Call(Hash.ADD_TEXT_ENTRY, "EMSTR_" + HeaderStringId, subject ?? "LSPD Traffic Division");
                Function.Call(Hash.ADD_TEXT_ENTRY, "EMSTR_" + BodyStringId, body ?? "Traffic citation history unavailable.");

                int defBase = EmailData + 1 + DefinitionSlot * EmailDefStride;
                GlobalVariable.Get(defBase + 0).Write<int>(BodyStringId);
                GlobalVariable.Get(defBase + 1).Write<int>(HeaderStringId);
                GlobalVariable.Get(defBase + 2).Write<int>(NativeSenderId);
                GlobalVariable.Get(defBase + 3).Write<int>(0);
                GlobalVariable.Get(defBase + 4).Write<int>(1);

                int campaignBase = EmailCampaigns + 1 + CampaignSlot * EmailCampaignStride;
                GlobalVariable.Get(campaignBase + 0).Write<int>(1);
                GlobalVariable.Get(campaignBase + 1).Write<int>(0);
                GlobalVariable.Get(campaignBase + 33).Write<int>(DefinitionSlot);
                GlobalVariable.Get(campaignBase + 42).Write<int>(1);

                int inboxBase = EmailInbox + 1 + owner * EmailInboxStride;
                int count = Math.Max(0, GlobalVariable.Get(inboxBase + 0).Read<int>());
                int slot = FindExistingSlot(inboxBase);
                bool existing = slot >= 0;
                if (!existing) slot = count % InboxSlots;

                // Keep one persistent history entry. Updating the same campaign
                // means older fines remain inside the body instead of filling the
                // sixteen-slot Story Mode inbox with duplicate LSPD rows.
                GlobalVariable.Get(inboxBase + 2 + slot).Write<int>(0);
                GlobalVariable.Get(inboxBase + 19 + slot).Write<int>(CampaignSlot);
                GlobalVariable.Get(inboxBase + 36 + slot).Write<int>(0);
                GlobalVariable.Get(inboxBase + 70 + slot).Write<int>(0);
                GlobalVariable.Get(inboxBase + 87 + slot).Write<int>(0);
                GlobalVariable.Get(inboxBase + 104 + slot).Write<int>(0);
                if (!existing) GlobalVariable.Get(inboxBase + 0).Write<int>(count + 1);

                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("Persistent iFruit inbox update failed safely: " + ex.Message);
                return false;
            }
        }

        private static int FindExistingSlot(int inboxBase)
        {
            for (int i = 0; i < InboxSlots; i++)
            {
                try
                {
                    if (GlobalVariable.Get(inboxBase + 19 + i).Read<int>() == CampaignSlot)
                        return i;
                }
                catch { }
            }
            return -1;
        }
    }
}
