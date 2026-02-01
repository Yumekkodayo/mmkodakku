using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Numerics;
using System.Linq;
using System.Diagnostics;
using KodakkuAssist.Module.GameEvent;
using KodakkuAssist.Module.Draw;
using KodakkuAssist.Module.GameOperate;
using KodakkuAssist.Script;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Dalamud.Utility.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.STD.Helper;
using Lumina.Data.Parsing;

namespace mmkodakku.Arcadion.Savage.Heavyweight.JP
{
    [ScriptType(name: "M4S 境中奇梦-仅标记 (拟人延迟版)",
        territorys: [1327], 
        guid: "d1d8375c-75e4-49a8-8764-aab85a982f0d", // 更新GUID以区分旧版
        version: "0.0.1.2",
        note: NoteStr,
        updateInfo: UpdateInfoStr,
        author: "Meowmi（Special Thanks Cicero）")]
    public class AAC_Heavyweight_M12_Savage_SimpleMark_Delay
    {
        // --- 常量定义区域 ---
        const string NoteStr =
        """
        M12S 境中奇梦 (四运) 日野专用标记脚本。
        
        功能：
        在四运开始时，检测连线分身位置并标记自己。
        新增“拟人延迟”功能，防止秒标被怀疑。
        """;

        const string UpdateInfoStr =
        """
        0.0.0.2: 新增自定义固定延迟与随机延迟功能，模拟人工操作。
        0.0.0.1: 代码结构规范化。
        """;

        // --- 用户设置区域 (User Settings) ---
        
        [UserSetting("启用脚本功能")]
        public bool EnableScript { get; set; } = true;

        [UserSetting("启用随机延迟 (模拟人工)")]
        public bool EnableRandomDelay { get; set; } = true;

        [UserSetting("固定延迟 (毫秒) - 仅在关闭随机延迟时生效", 1000, 3000)] 
        public int FixedDelay { get; set; } = 1500;

        [UserSetting("随机延迟最小值 (毫秒)", 1000, 3000)]
        public int RandomMin { get; set; } = 1000;

        [UserSetting("随机延迟最大值 (毫秒)", 1000, 5000)]
        public int RandomMax { get; set; } = 3000;

        [UserSetting("调试模式 (输出详细日志)")]
        public bool DebugMode { get; set; } = false;

        // --- 内部变量区域 ---
        
        private volatile bool _isInPhase4 = false;
        private static readonly Vector3 ArenaCenter = new Vector3(100, 0, 100);
        private readonly Random _random = new Random();

        // --- 初始化 (Init) ---

        public void Init(ScriptAccessory accessory)
        {
            _isInPhase4 = false;
        }

        // --- 核心逻辑方法 (Script Methods) ---

        [ScriptMethod(name: "四运阶段识别", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:46345"], userControl: false)]
        public void Phase4Control(Event @event, ScriptAccessory accessory)
        {
            if (!EnableScript) return;
            _isInPhase4 = true;
            if (DebugMode) accessory.Log.Debug("检测到境中奇梦读条，标记逻辑已激活。");
        }

        [ScriptMethod(name: "初始分身标记 (含延迟)", eventType: EventTypeEnum.Tether, eventCondition: ["Id:0175"])]
        public void MarkBasedOnInitialClone(Event @event, ScriptAccessory accessory)
        {
            if (!EnableScript || !_isInPhase4) return;

            // 1. 目标检查
            if (!TryParseObjectId(@event["TargetId"], out var targetId)) return;
            // 提前获取 Data.Me，避免在 Task 中访问 Accessor 可能存在的线程安全问题
            uint myId = accessory.Data.Me; 
            if (targetId != myId) return;

            // 2. 来源检查
            if (!TryParseObjectId(@event["SourceId"], out var sourceId)) return;
            var sourceObject = accessory.Data.Objects.SearchById(sourceId);
            if (sourceObject == null || sourceObject.DataId != 19210) return;

            // 3. 数据解析
            Vector3 sourcePosition;
            try
            {
                sourcePosition = JsonConvert.DeserializeObject<Vector3>(@event["SourcePosition"]);
            }
            catch { return; }

            // 4. 计算逻辑 (主线程快速完成)
            int directionIndex = GetDirectionIndex(sourcePosition, ArenaCenter, 8);
            MarkType markToSet = directionIndex switch
            {
                0 => MarkType.Attack4, // N
                1 => MarkType.Attack1, // NE
                2 => MarkType.Stop2,   // E
                3 => MarkType.Attack2, // SE
                4 => MarkType.Stop1,   // S
                5 => MarkType.Bind1,   // SW
                6 => MarkType.Bind2,   // W
                7 => MarkType.Attack3, // NW
                _ => MarkType.Attack1
            };

            // 5. 延迟执行逻辑 (拟人化核心)
            int delayMs = FixedDelay;
            
            if (EnableRandomDelay)
            {
                // 简单的容错处理，防止 Min > Max 导致报错
                int min = Math.Min(RandomMin, RandomMax);
                int max = Math.Max(RandomMin, RandomMax);
                delayMs = _random.Next(min, max);
            }

            if (DebugMode)
            {
                accessory.Log.Debug($"计算完成。方位:{directionIndex}, 标记:{markToSet}, 将在 {delayMs}ms 后执行。");
            }

            // 开启后台任务进行等待和标记，不阻塞主线程
            Task.Run(async () =>
            {
                await Task.Delay(delayMs);
                accessory.Method.Mark(myId, markToSet);
                if (DebugMode) accessory.Log.Debug($"延迟结束，已执行标记。");
            });
        }

        // --- 辅助方法 ---

        private static bool TryParseObjectId(string? rawHexId, out ulong result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(rawHexId)) return false;
            string hexId = rawHexId.Trim();
            if (hexId.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hexId = hexId.Substring(2);
            return ulong.TryParse(hexId, System.Globalization.NumberStyles.HexNumber, null, out result);
        }

        private static int GetDirectionIndex(Vector3 position, Vector3 center, int numberOfDirections)
        {
            double angle = Math.Atan2(position.X - center.X, position.Z - center.Z);
            double faction = (numberOfDirections / 2.0d) - (numberOfDirections / 2.0d) * angle / Math.PI;
            return (int)((Math.Round(faction) % numberOfDirections + numberOfDirections) % numberOfDirections);
        }
    }
}
