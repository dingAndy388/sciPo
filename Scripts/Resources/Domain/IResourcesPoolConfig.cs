using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Resources.Domain
{
    public interface IResourcesPoolConfig
    {
        List<IResourceConfig> Resources { get;}

        /// <summary>
        /// （v0.3 / WP-3.9 / `TIME-14`）**月度经济结算参数**（`Config/Resources.json` 的 `Settlement` 段）。
        /// <para>空 = 该表没有写结算参数：结算器不会向人口收维护费（校验器给 warning 提示这是"填了不生效"的隐患）。</para>
        /// </summary>
        ISettlementConfig Settlement { get; }
    }
}
