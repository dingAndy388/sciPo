using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SciencePotato.Scripts.Common.Domain
{
    /// <summary>
    /// （v0.3 / WP-2.2）任务仓储：**按地图分文件的快照集合**，键 = <see cref="TaskSnapshot.Key"/>（`Type:UId:Id`）。
    /// <para>旧实现以 <c>Id</c> 作键（`TIME-03`：同名任务互相覆盖）、用写 null 代替删除（`TIME-04`）；
    /// 本接口的语义已按"键唯一 + 真删除 + 可按谓词批量清理"重写。</para>
    /// </summary>
    public interface ITaskRepository
    {
        List<TaskSnapshot> GetCurrentTasks(string mapId);
        void AddTask(string mapId, TaskSnapshot task);
        void RemoveTask(string mapId, TaskSnapshot task);

        /// <summary>
        /// （v0.3 / WP-2.2）按谓词批量移除（`TIME-02` 范围注销：实体消失时清掉它名下的所有任务）。
        /// </summary>
        /// <returns>实际删除的条数。</returns>
        int RemoveTasks(string mapId, Func<TaskSnapshot, bool> match);
    }
}
