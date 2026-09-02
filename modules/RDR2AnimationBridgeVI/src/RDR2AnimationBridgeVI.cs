using GTA;
using GTA.Native;
using System;
using System.IO;
using System.Windows.Forms;

namespace VOX.RDR2AnimationBridgeVI
{
    public sealed class RDR2AnimationBridgeVIScript : Script
    {
        private const string DataDir = "scripts\\RDR2AnimationBridgeVI";
        private const string ConfigPath = "scripts\\RDR2AnimationBridgeVI.ini";
        private const string LogPath = DataDir + "\\RDR2AnimationBridgeVI.log";

        private string _dict = "vox_rdr2_bridge";
        private string _clip = "";
        private Keys _key = Keys.F8;
        private bool _pending;
        private int _requestedAt;

        public RDR2AnimationBridgeVIScript()
        {
            Directory.CreateDirectory(DataDir);
            LoadConfig();
            Interval = 10;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
            Log("RDR2 Animation Bridge VI v0.1.0 loaded. Dict=" + _dict + " Clip=" + _clip + " Key=" + _key);
            Notify(string.IsNullOrWhiteSpace(_clip)
                ? "RDR2 Bridge charge, mais aucun clip n'est configure."
                : "RDR2 Bridge pret - " + _key + " pour jouer l'animation.");
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != _key) return;
            if (string.IsNullOrWhiteSpace(_dict) || string.IsNullOrWhiteSpace(_clip))
            {
                Notify("RDR2 Bridge : dictionnaire/clip manquant.");
                return;
            }

            try
            {
                Function.Call(Hash.REQUEST_ANIM_DICT, _dict);
                _pending = true;
                _requestedAt = Game.GameTime;
                Log("Requested anim dictionary " + _dict + " for clip " + _clip + ".");
            }
            catch (Exception ex)
            {
                _pending = false;
                Log("REQUEST_ANIM_DICT failed: " + ex);
                Notify("RDR2 Bridge : REQUEST_ANIM_DICT a echoue.");
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_pending) return;
            try
            {
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, _dict))
                {
                    _pending = false;
                    var player = Game.LocalPlayerPed;
                    if (player == null || !player.Exists() || player.IsDead)
                    {
                        Notify("RDR2 Bridge : joueur indisponible.");
                        return;
                    }

                    Function.Call(Hash.CLEAR_PED_TASKS, player.Handle);
                    Function.Call(Hash.TASK_PLAY_ANIM,
                        player.Handle,
                        _dict,
                        _clip,
                        8.0f,
                        -8.0f,
                        -1,
                        1,
                        0.0f,
                        false,
                        false,
                        false);

                    Log("TASK_PLAY_ANIM fired successfully: " + _dict + " / " + _clip);
                    Notify("RDR2 Bridge : animation lancee. Observe le personnage.");
                    return;
                }

                if (Game.GameTime - _requestedAt > 7000)
                {
                    _pending = false;
                    Log("Anim dictionary load timeout: " + _dict);
                    Notify("RDR2 Bridge : le dictionnaire YCD ne s'est pas charge.");
                }
            }
            catch (Exception ex)
            {
                _pending = false;
                Log("Playback tick failed: " + ex);
                Notify("RDR2 Bridge : erreur pendant la lecture.");
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                foreach (var raw in File.ReadAllLines(ConfigPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("[")) continue;
                    var split = line.IndexOf('=');
                    if (split <= 0) continue;
                    var name = line.Substring(0, split).Trim();
                    var value = line.Substring(split + 1).Trim();
                    if (name.Equals("Dict", StringComparison.OrdinalIgnoreCase)) _dict = value;
                    else if (name.Equals("Clip", StringComparison.OrdinalIgnoreCase)) _clip = value;
                    else if (name.Equals("Key", StringComparison.OrdinalIgnoreCase) && Enum.TryParse(value, true, out Keys parsed)) _key = parsed;
                }
            }
            catch (Exception ex)
            {
                Log("Config read failed: " + ex);
            }
        }

        private static void Notify(string text)
        {
            try
            {
                Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
                Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
                Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, false, false);
            }
            catch { }
        }

        private static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + text + Environment.NewLine);
            }
            catch { }
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _pending = false;
        }
    }
}
