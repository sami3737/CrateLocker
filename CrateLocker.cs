using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Crate Locker", "sami37", "1.0.2")]
    public class CrateLocker : RustPlugin
    {
        private Dictionary<uint, Dictionary<string, float>> TempDamage = new Dictionary<uint, Dictionary<string, float>>();
        private Dictionary<string, DamageCounters> killsDictionary = new Dictionary<string, DamageCounters>();
        private Dictionary<uint, BasePlayer> lastHit = new Dictionary<uint, BasePlayer>();
        private List<TimeKill> TimeKilled = new List<TimeKill>();
        private Dictionary<uint, BasePlayer> lockedCrate = new Dictionary<uint, BasePlayer>();
        private bool ExcludeAdmin, ExcludeMod;

        class TimeKill
        {
            public uint UID { get; set; }
            public DateTime date { get; set; }
            public Vector3 Position { get; set; }
            public string PlayerID { get; set; }
        }

        class DamageCounters
        {
            public Dictionary<uint, Dictionary<ulong, float>> DamageList;
        }

        #region ConfigFunction

        string ListToString<T>(List<T> list, int first = 0, string seperator = ", ") => string.Join(seperator,
            (from val in list select val.ToString()).Skip(first).ToArray());

        void SetConfig(params object[] args)
        {
            List<string> stringArgs = (from arg in args select arg.ToString()).ToList();
            stringArgs.RemoveAt(args.Length - 1);
            if (Config.Get(stringArgs.ToArray()) == null) Config.Set(args);
        }

        T GetConfig<T>(T defaultVal, params object[] args)
        {
            List<string> stringArgs = (from arg in args select arg.ToString()).ToList();
            if (Config.Get(stringArgs.ToArray()) == null)
            {
                PrintError(
                    $"The plugin failed to read something from the config: {ListToString(stringArgs, 0, "/")}{Environment.NewLine}Please reload the plugin and see if this message is still showing. If so, please post this into the support thread of this plugin.");
                return defaultVal;
            }

            return (T)Convert.ChangeType(Config.Get(stringArgs.ToArray()), typeof(T));
        }

        #endregion

        protected override void LoadDefaultConfig()
        {
            SetConfig("Exclude Admin", true);
            SetConfig("Exclude Moderator", true);
            SaveConfig();
        }

        void Loaded()
        {
            ExcludeAdmin = GetConfig(true, "Exclude Admin");
            ExcludeMod = GetConfig(true, "Exclude Moderator");
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"TopDamage", "{0} has been destroyed by {1} and crate are locked to him." },
                {"NotAllowed", "This crate is not your." }
            }, this);
        }

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity as BaseHelicopter != null || entity as BradleyAPC != null)
            {
                if (!info.InitiatorPlayer) return null;
                if (TempDamage == null) return null;
                if (!TempDamage.ContainsKey(entity.net.ID))
                    TempDamage.Add(entity.net.ID, new Dictionary<string, float>());
                if (!TempDamage[entity.net.ID].ContainsKey(info.InitiatorPlayer.displayName) && (info.damageTypes != null && info.damageTypes.Has(info.damageTypes.GetMajorityDamageType()) &&
                    info.damageTypes.Get(info.damageTypes.GetMajorityDamageType()) > 0.0))
                    TempDamage[entity.net.ID].Add(info.InitiatorPlayer.displayName,
                        info.damageTypes.Get(info.damageTypes.GetMajorityDamageType()));
                else if (info.damageTypes != null && info.damageTypes.Has(info.damageTypes.GetMajorityDamageType()))
                    TempDamage[entity.net.ID][info.InitiatorPlayer.displayName] +=
                        info.damageTypes.Get(info.damageTypes.GetMajorityDamageType());


                if (info.damageTypes != null && info.damageTypes.Has(info.damageTypes.GetMajorityDamageType()) && info.damageTypes.Get(info.damageTypes.GetMajorityDamageType()) > 0.0)
                {
                    var type = entity as BradleyAPC != null ? "bradley" : "heli";
                    if (entity as BradleyAPC != null)
                        if (!killsDictionary.ContainsKey(type))
                        {
                            killsDictionary.Add(type, new DamageCounters
                            {
                                DamageList = new Dictionary<uint, Dictionary<ulong, float>>
                                {
                                    {
                                        entity.net.ID, new Dictionary<ulong, float>
                                        {
                                            {
                                                info.InitiatorPlayer.userID,
                                                info.damageTypes.Get(info.damageTypes.GetMajorityDamageType())
                                            }
                                        }
                                    }
                                }
                            });
                        }
                        else
                        {
                            if (killsDictionary.ContainsKey(type))
                            {
                                var damageList = killsDictionary[type].DamageList;
                                if (damageList != null && damageList.ContainsKey(entity.net.ID))
                                    if (killsDictionary[type].DamageList[entity.net.ID].ContainsKey(info.InitiatorPlayer.userID))
                                    {
                                        killsDictionary[type].DamageList[entity.net.ID][info.InitiatorPlayer.userID] +=
                                            info.damageTypes.Has(info.damageTypes.GetMajorityDamageType()) ? info.damageTypes.Get(info.damageTypes.GetMajorityDamageType()) : 0;
                                    }
                                    else
                                    {
                                        killsDictionary[type].DamageList[entity.net.ID].Add(
                                            info.InitiatorPlayer.userID,
                                            info.damageTypes.Has(info.damageTypes.GetMajorityDamageType()) ? info.damageTypes.Get(info.damageTypes.GetMajorityDamageType()) : 0);
                                    }
                                else
                                {
                                    killsDictionary[type].DamageList.Add(entity.net.ID,
                                        new Dictionary<ulong, float>
                                        {
                                            {
                                                info.InitiatorPlayer.userID,
                                                info.damageTypes.Has(info.damageTypes.GetMajorityDamageType()) ? info.damageTypes.Get(info.damageTypes.GetMajorityDamageType()) : 0
                                }
                                        });
                                }
                            }
                        }
                }
            }

            return null;
        }

        void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player.net.connection.authLevel == 1 && ExcludeMod) return;
            if (player.net.connection.authLevel == 2 && ExcludeAdmin) return;
            if (lockedCrate.ContainsKey(entity.net.ID) && lockedCrate[entity.net.ID] != player)
            {
                SendReply(player, lang.GetMessage("NotAllowed", this, player.UserIDString));
                NextTick(player.EndLooting);
            }
        }

        void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity.PrefabName.Contains("bradley_crate"))
            {
                NextTick(() =>
                {
                    TimeKilled = TimeKilled.OrderBy(
                        x => Vector2.Distance(entity.transform.position, x.Position)
                    ).ToList();

                    if (Vector3.Distance(entity.transform.position, TimeKilled[0].Position) <= 10.0f)
                    {
                        lockedCrate.Add(entity.net.ID, BasePlayer.Find(TimeKilled[0].PlayerID));
                    }
                });
            }

            if (entity.PrefabName.Contains("heli_crate"))
            {
                NextTick(() =>
                {
                    TimeKilled = TimeKilled.OrderBy(
                        x => Vector2.Distance(entity.transform.position, x.Position)
                    ).ToList();

                    if (Vector3.Distance(entity.transform.position, TimeKilled[0].Position) <= 30.0f)
                    {
                        lockedCrate.Add(entity.net.ID, BasePlayer.Find(TimeKilled[0].PlayerID));
                    }
                });
            }
        }

        void OnEntityKill(BaseNetworkable entity)
        {
            if (entity is BaseHelicopter || entity is BradleyAPC)
            {
                var type = entity as BradleyAPC != null ? "Bradley" : "Helicopter";
                if (!TempDamage.ContainsKey(entity.net.ID)) return;
                var items = from pair in TempDamage[entity.net.ID]
                            orderby pair.Value descending
                            select pair;

                foreach (var player in BasePlayer.activePlayerList.Where(x => x.net.connection.authLevel > 0))
                {
                    if (TempDamage[entity.net.ID].Count >= 1)
                    {
                        int i = 0;
                        foreach (KeyValuePair<string, float> pair in items)
                        {
                            if(TimeKilled == null)
                                TimeKilled = new List<TimeKill>();
                            if (TimeKilled.Find(x => x.UID == entity.net.ID) == null)
                            {
                                TimeKilled.Add(new TimeKill{
                                    date = DateTime.Now,
                                    UID = entity.net.ID, Position = entity.transform.position,
                                    PlayerID = pair.Key
                                });
                            }
                            var p = BasePlayer.Find(pair.Key);
                            if (i == 1)
                                break;
                            SendReply(player, string.Format(lang.GetMessage("TopDamage", this, player.UserIDString),
                                type, p == null
                                    ? "Unknow"
                                    : $"{p.displayName}"));
                            i++;
                        }
                    }
                }
            }
        }
    }
}