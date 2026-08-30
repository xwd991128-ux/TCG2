using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TcgEngine.Workshop;

namespace TcgEngine.UI
{
    /// <summary>
    /// 卡池管理系统面板（游戏内 Unity 组件版）
    /// 显示内置卡池（按卡包划分）+ 本地卡池（JSON 文件），
    /// 支持多选导入/导出、单个导出、删除本地卡池。
    /// 面板由 Editor 工具（CardPoolPanelBuilder）在场景中用 Unity UI 组件搭建，
    /// 所有 UI 引用在 Inspector 中手动绑定（与卡牌管理 CollectionPanel 一致），
    /// 运行时不动态创建界面。
    /// </summary>
    public class CardPoolPanel : UIPanel
    {
        [Header("列表")]
        public ScrollRect scroll_rect;          // 滚动区域
        public RectTransform scroll_content;    // 列表容器（Content）
        public GameObject line_template;        // 行模板（隐藏，运行时复制）

        [Header("标题")]
        public Text title_text;                 // 标题文字
        public Button close_btn;                // 关闭按钮

        [Header("工具栏")]
        public Button select_all_btn;           // 全选
        public Button select_none_btn;          // 全不选
        public Button import_btn;               // 导入
        public Button export_btn;               // 批量导出
        public Text status_text;                // 底部状态提示

        private readonly float item_h = 50f;

        private List<PoolEntry> pool_entries = new List<PoolEntry>();
        private static CardPoolPanel instance;

        private class PoolEntry
        {
            public CardPoolIO.PoolInfo info;
            public Toggle toggle;
            public GameObject line;
        }

        public static CardPoolPanel Get() { return instance; }

        protected override void Awake()
        {
            base.Awake();
            instance = this;

            if (close_btn != null) close_btn.onClick.AddListener(() => Hide());
            if (select_all_btn != null) select_all_btn.onClick.AddListener(() => SetAllSelected(true));
            if (select_none_btn != null) select_none_btn.onClick.AddListener(() => SetAllSelected(false));
            if (import_btn != null) import_btn.onClick.AddListener(OnImport);
            if (export_btn != null) export_btn.onClick.AddListener(OnExportSelected);
        }

        public override void Show(bool instant = false)
        {
            base.Show(instant);
            RefreshList();
        }

        // ---------------- 列表 ----------------

        private void RefreshList()
        {
            if (scroll_content == null)
                return;

            //清除旧行（保留模板）
            for (int i = scroll_content.childCount - 1; i >= 0; i--)
            {
                Transform child = scroll_content.GetChild(i);
                if (child != null && child.gameObject != line_template)
                    Destroy(child.gameObject);
            }
            pool_entries.Clear();

            List<CardPoolIO.PoolInfo> pools = new List<CardPoolIO.PoolInfo>();
            pools.AddRange(CardPoolIO.GetBuiltinPools());
            pools.AddRange(CardPoolIO.GetLocalPools());

            foreach (CardPoolIO.PoolInfo info in pools)
                CreatePoolLine(info);

            SetStatus(pools.Count > 0 ? "共 " + pools.Count + " 个卡池" : "暂无卡池，点击「导入」添加本地卡池");
        }

        private void CreatePoolLine(CardPoolIO.PoolInfo info)
        {
            if (line_template == null)
                return;

            GameObject line = Instantiate(line_template, scroll_content);
            line.name = "PoolLine_" + info.name;
            line.SetActive(true);

            PoolEntry entry = new PoolEntry();
            entry.info = info;
            entry.line = line;
            entry.toggle = line.transform.Find("Toggle")?.GetComponent<Toggle>();

            Text name_text = line.transform.Find("NameText")?.GetComponent<Text>();
            Text count_text = line.transform.Find("CountText")?.GetComponent<Text>();
            if (name_text != null)
                name_text.text = info.name + (info.IsReadonly ? "  <color=#9FD5FF>（内置）</color>" : "  <color=#FFE08A>（本地）</color>");
            if (count_text != null)
                count_text.text = info.card_count + " 张";

            Button export_btn = line.transform.Find("ExportBtn")?.GetComponent<Button>();
            if (export_btn != null)
                export_btn.onClick.AddListener(() => OnExportOne(info));

            Button del_btn = line.transform.Find("DeleteBtn")?.GetComponent<Button>();
            if (del_btn != null)
            {
                if (info.IsReadonly)
                    del_btn.gameObject.SetActive(false);
                else
                    del_btn.onClick.AddListener(() => OnDelete(info));
            }

            pool_entries.Add(entry);
        }

        private void SetAllSelected(bool selected)
        {
            foreach (PoolEntry entry in pool_entries)
            {
                if (entry.toggle != null)
                    entry.toggle.isOn = selected;
            }
        }

        private List<CardPoolIO.PoolInfo> GetSelectedPools()
        {
            List<CardPoolIO.PoolInfo> list = new List<CardPoolIO.PoolInfo>();
            foreach (PoolEntry entry in pool_entries)
            {
                if (entry.toggle != null && entry.toggle.isOn)
                    list.Add(entry.info);
            }
            return list;
        }

        // ---------------- 导入/导出/删除 ----------------

        private void OnImport()
        {
            string[] files = FileDialogTool.OpenFiles("选择要导入的卡池文件", "卡池文件 (*.json)|*.json", true);
            if (files == null || files.Length == 0)
            {
                SetStatus("未选择文件，导入已取消");
                return;
            }

            int copied = 0;
            int total = 0;
            foreach (string file in files)
            {
                try
                {
                    //导入的文件复制到本地卡池目录，保证重启后自动加载（持久化）
                    string target = file;
                    string folder = CardPoolIO.SaveFolder;
                    if (!Path.GetDirectoryName(file).Equals(folder, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(folder);
                        target = Path.Combine(folder, Path.GetFileName(file));
                        File.Copy(file, target, true);
                        copied++;
                    }

                    int before = CardData.GetAll().Count;
                    CardPoolIO.ImportFromFile(target, true); //授予拥有数量，使构筑界面立即可用
                    total += CardData.GetAll().Count - before;
                }
                catch (Exception e)
                {
                    Debug.LogError("导入失败: " + file + " " + e.Message);
                    SetStatus("导入失败: " + Path.GetFileName(file) + " " + e.Message);
                }
            }
            SetStatus("已导入 " + files.Length + " 个文件，新增 " + total + " 张卡（复制 " + copied + " 个到本地卡池目录）");
            RefreshList();
        }

        private void OnExportOne(CardPoolIO.PoolInfo info)
        {
            string folder = FileDialogTool.SelectFolder("选择导出保存目录");
            if (string.IsNullOrEmpty(folder))
                folder = CardPoolIO.SaveFolder; //不可用/取消时降级到游戏内目录
            if (ExportPool(info, folder))
                SetStatus("已导出卡池: " + info.name + " -> " + folder);
        }

        private void OnExportSelected()
        {
            List<CardPoolIO.PoolInfo> selected = GetSelectedPools();
            if (selected.Count == 0)
            {
                SetStatus("请先勾选要导出的卡池");
                return;
            }

            string folder = FileDialogTool.SelectFolder("选择导出保存目录");
            if (string.IsNullOrEmpty(folder))
                folder = CardPoolIO.SaveFolder;

            int count = 0;
            foreach (CardPoolIO.PoolInfo info in selected)
            {
                if (ExportPool(info, folder))
                    count++;
            }
            SetStatus("已导出 " + count + " 个卡池到: " + folder);
        }

        /// <summary>导出一个卡池到指定目录（内置重新序列化，本地直接复制文件）</summary>
        private bool ExportPool(CardPoolIO.PoolInfo info, string folder)
        {
            try
            {
                if (info.IsReadonly)
                {
                    if (info.cards == null || info.cards.Count == 0)
                        return false;
                    CardPoolIO.ExportToPath(info.cards, info.name, folder);
                }
                else
                {
                    if (string.IsNullOrEmpty(info.file) || !File.Exists(info.file))
                        return false;
                    Directory.CreateDirectory(folder);
                    string dest = Path.Combine(folder, info.name + ".json");
                    File.Copy(info.file, dest, true);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("导出失败: " + info.name + " " + e.Message);
                return false;
            }
        }

        private void OnDelete(CardPoolIO.PoolInfo info)
        {
            if (CardPoolIO.DeletePoolFile(info.file))
            {
                SetStatus("已删除卡池: " + info.name);
                RefreshList();
            }
        }

        private void SetStatus(string msg)
        {
            if (status_text != null)
                status_text.text = msg;
        }
    }
}
