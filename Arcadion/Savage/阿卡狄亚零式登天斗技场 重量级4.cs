using System;
using System.Threading;
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
    [ScriptType(name: "M12S 境中奇梦-仅标记",
        territorys: [1327], 
        guid: "b01cf99c-b9e5-4b5f-8d61-4c5e1060268e", // 唯一标识符
        version: "0.0.0.1",
        note: NoteStr,
        updateInfo: UpdateInfoStr,
        author: "meowmi(special thanks灵视)")]
    public class AAC_Heavyweight_M4_Savage_SimpleMark
    {
        // --- 常量定义区域 ---
        const string NoteStr =
        """
        境中奇梦 (本体四运) 专用标记脚本。
        功能：
        仅在四运开始时，检测连线分身的初始位置，根据方位对自己进行标记（日野Game8攻略）。
        不包含任何绘图、TTS或自动移动指引。
        """;

        const string UpdateInfoStr =
        """
        0.0.1.1: 代码结构规范化，增加用户开关。
        0.0.1.0: 初始版本。
        """;

        // --- 用户设置区域 (User Settings) ---
        
        [UserSetting("启用脚本功能")]
        public bool EnableScript { get; set; } = true;

        [UserSetting("调试模式 (输出详细日志)")]
        public bool DebugMode { get; set; } = false;

        // --- 内部变量区域 ---
        
        // 标记：是否进入了四运阶段
        private volatile bool _isInPhase4 = false;
        
        // 场地中心常数
        private static readonly Vector3 ArenaCenter = new Vector3(100, 0, 100);

        // --- 初始化 (Init) ---

        public void Init(ScriptAccessory accessory)
        {
            _isInPhase4 = false;
        }

        // --- 核心逻辑方法 (Script Methods) ---

        /// <summary>
        /// 阶段控制：监测 "境中奇梦" (Idyllic Dream) 读条 (ActionId: 46345)
        /// </summary>
        [ScriptMethod(name: "四运阶段识别", eventType: EventTypeEnum.StartCasting, eventCondition: ["ActionId:46345"], userControl: false)]
        public void Phase4Control(Event @event, ScriptAccessory accessory)
        {
            if (!EnableScript) return;

            _isInPhase4 = true;
            
            if (DebugMode)
            {
                accessory.Log.Debug("检测到境中奇梦读条，标记逻辑已激活。");
            }
        }

        /// <summary>
        /// 核心逻辑：监听连线 (Tether ID: 0175) 并标记
        /// </summary>
        [ScriptMethod(name: "初始分身标记", eventType: EventTypeEnum.Tether, eventCondition: ["Id:0175"])]
        public void MarkBasedOnInitialClone(Event @event, ScriptAccessory accessory)
        {
            // 基础检查：脚本开关、阶段检查
            if (!EnableScript || !_isInPhase4) return;

            // 1. 目标检查：连线目标必须是“我”
            if (!TryParseObjectId(@event["TargetId"], out var targetId)) return;
            if (targetId != accessory.Data.Me) return;

            // 2. 来源检查：连线源头必须是分身 (DataId: 19210)
            if (!TryParseObjectId(@event["SourceId"], out var sourceId)) return;
            var sourceObject = accessory.Data.Objects.SearchById(sourceId);
            if (sourceObject == null || sourceObject.DataId != 19210) return;

            // 3. 数据解析：获取分身坐标
            Vector3 sourcePosition;
            try
            {
                sourcePosition = JsonConvert.DeserializeObject<Vector3>(@event["SourcePosition"]);
            }
            catch (Exception ex)
            {
                if (DebugMode) accessory.Log.Debug($"[M4S定制] 坐标解析失败: {ex.Message}");
                return;
            }

            // 4. 方位计算 (0-7)
            int directionIndex = GetDirectionIndex(sourcePosition, ArenaCenter, 8);

            // 5. 标记映射逻辑
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
                _ => MarkType.Attack1  // 默认 Fallback
            };

            // 6. 执行标记
            accessory.Method.Mark(accessory.Data.Me, markToSet);

            if (DebugMode)
            {
                accessory.Log.Debug($"连线方位: {directionIndex}, 执行标记: {markToSet}");
            }
        }

        // --- 辅助方法 (Helpers) ---

        /// <summary>
        /// 解析 16 进制 ObjectId
        /// </summary>
        private static bool TryParseObjectId(string? rawHexId, out ulong result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(rawHexId)) return false;
            
            string hexId = rawHexId.Trim();
            if (hexId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hexId = hexId.Substring(2);
            }
            
            return ulong.TryParse(hexId, System.Globalization.NumberStyles.HexNumber, null, out result);
        }

        /// <summary>
        /// 计算坐标方位索引 (0 = 北, 顺时针增加)
        /// </summary>
        private static int GetDirectionIndex(Vector3 position, Vector3 center, int numberOfDirections)
        {
            // Atan2 返回的是 (-PI, PI]
            double angle = Math.Atan2(position.X - center.X, position.Z - center.Z);
            
            // 将角度转换为 0 到 numberOfDirections-1 的索引
            // 游戏坐标系通常 Z 轴向下为正，需要根据具体数学库调整，但此处沿用原脚本验证过的算法
            double faction = (numberOfDirections / 2.0d) - (numberOfDirections / 2.0d) * angle / Math.PI;
            
            return (int)((Math.Round(faction) % numberOfDirections + numberOfDirections) % numberOfDirections);
        }
    }
}
