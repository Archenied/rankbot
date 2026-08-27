// RankBotSetup - one window that configures everything RankBot needs.
// Builds with the compiler already inside Windows. See build.bat.
//
// C# 5 only (no string interpolation, no ?. operator) because the in-box
// compiler is the old one.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class Role
{
    public string Id;
    public string Name;
    public int Rank;

    public override string ToString()
    {
        return Name + "  (rank " + Rank + ")";
    }
}

public class SetupForm : Form
{
    // --- where everything lives ---------------------------------------------
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static string P(string name) { return Path.Combine(Root, name); }

    // --- controls ------------------------------------------------------------
    TextBox txtGroupId, txtApiKey, txtCookie, txtLog, txtNeverTouch;
    Button btnCheck, btnTestKey, btnTestCookie, btnSave, btnRun, btnAdd, btnRemove, btnPower, btnFolder, btnPrepare;

    // Files that must never leave this computer. Dragging files into the GitHub
    // website ignores .gitignore completely, so the only safe way is to hand
    // over a folder that never contained them in the first place.
    static readonly string[] NeverUpload = { "apikey.txt", "cookie.txt", "crash.txt" };
    const string UploadFolder = "upload-to-github";
    bool active = true;
    Label lblGroupInfo, lblKeyStatus, lblCookieStatus;
    CheckBox chkAuto, chkWatchlist, chkDryRun;
    ComboBox cboFrom, cboTo;
    DataGridView grid;
    DataGridViewComboBoxColumn rankColumn;

    List<Role> roles = new List<Role>();

    public SetupForm()
    {
        // Roblox refuses anything older than TLS 1.2.
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;

        Text = "RankBot Setup";
        // Tall enough that the watchlist grid still shows several rows once the
        // fixed-height sections below it have taken their share.
        Size = new Size(900, 900);
        MinimumSize = new Size(860, 820);
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        LoadEverything();
    }

    // ========================================================================
    // Building the window
    // ========================================================================

    void BuildUi()
    {
        var page = new TableLayoutPanel();
        page.Dock = DockStyle.Fill;
        page.Padding = new Padding(12);
        page.ColumnCount = 1;
        page.RowCount = 7;
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // group
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // login
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // auto-rank
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // watchlist
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f)); // never touch
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 140f)); // log

        page.Controls.Add(BuildGroupBox(), 0, 0);
        page.Controls.Add(BuildLoginBox(), 0, 1);
        page.Controls.Add(BuildAutoRankBox(), 0, 2);
        page.Controls.Add(BuildWatchlistBox(), 0, 3);
        page.Controls.Add(BuildNeverTouchBox(), 0, 4);
        page.Controls.Add(BuildButtonRow(), 0, 5);
        page.Controls.Add(BuildLogBox(), 0, 6);

        Controls.Add(page);
    }

    static GroupBox Section(string title, int height)
    {
        var box = new GroupBox();
        box.Text = title;
        box.Dock = DockStyle.Fill;
        box.Height = height;
        box.Padding = new Padding(10, 6, 10, 10);
        return box;
    }

    static Label Caption(string text, int x, int y, int width)
    {
        var label = new Label();
        label.Text = text;
        label.Location = new Point(x, y);
        label.Size = new Size(width, 20);
        label.TextAlign = ContentAlignment.MiddleLeft;
        return label;
    }

    Control BuildGroupBox()
    {
        var box = Section("Step 1 - Your group", 92);

        box.Controls.Add(Caption("Group ID:", 12, 28, 70));

        txtGroupId = new TextBox();
        txtGroupId.Location = new Point(86, 26);
        txtGroupId.Size = new Size(160, 24);
        box.Controls.Add(txtGroupId);

        btnCheck = new Button();
        btnCheck.Text = "Check group";
        btnCheck.Location = new Point(256, 25);
        btnCheck.Size = new Size(110, 26);
        btnCheck.Click += CheckGroupClicked;
        box.Controls.Add(btnCheck);

        lblGroupInfo = Caption("Type your group ID and press Check.", 378, 28, 330);
        lblGroupInfo.ForeColor = Color.DimGray;
        box.Controls.Add(lblGroupInfo);

        return box;
    }

    Control BuildLoginBox()
    {
        var box = Section("Step 2 - How RankBot logs in", 150);

        box.Controls.Add(Caption("API key:", 12, 26, 70));
        txtApiKey = new TextBox();
        txtApiKey.Location = new Point(86, 24);
        txtApiKey.Size = new Size(400, 24);
        txtApiKey.UseSystemPasswordChar = true;
        box.Controls.Add(txtApiKey);

        btnTestKey = new Button();
        btnTestKey.Text = "Test";
        btnTestKey.Location = new Point(494, 23);
        btnTestKey.Size = new Size(60, 26);
        btnTestKey.Click += TestKeyClicked;
        box.Controls.Add(btnTestKey);

        lblKeyStatus = Caption("", 562, 26, 160);
        box.Controls.Add(lblKeyStatus);

        box.Controls.Add(Caption("Cookie:", 12, 58, 70));
        txtCookie = new TextBox();
        txtCookie.Location = new Point(86, 56);
        txtCookie.Size = new Size(400, 24);
        txtCookie.UseSystemPasswordChar = true;
        box.Controls.Add(txtCookie);

        btnTestCookie = new Button();
        btnTestCookie.Text = "Test";
        btnTestCookie.Location = new Point(494, 55);
        btnTestCookie.Size = new Size(60, 26);
        btnTestCookie.Click += TestCookieClicked;
        box.Controls.Add(btnTestCookie);

        lblCookieStatus = Caption("", 562, 58, 160);
        box.Controls.Add(lblCookieStatus);

        var note = Caption(
            "The API key is used first. The cookie is only a backup - leave it empty unless you need it.",
            12, 86, 700);
        note.ForeColor = Color.DimGray;
        box.Controls.Add(note);

        var warn = Caption(
            "WARNING: your cookie unlocks your WHOLE Roblox account. Never show it to anyone, ever.",
            12, 106, 700);
        warn.ForeColor = Color.Firebrick;
        box.Controls.Add(warn);

        return box;
    }

    Control BuildAutoRankBox()
    {
        var box = Section("Step 3 - Rank people who join", 92);

        chkAuto = new CheckBox();
        chkAuto.Text = "Automatically rank players who join the group";
        chkAuto.Location = new Point(12, 24);
        chkAuto.Size = new Size(320, 22);
        chkAuto.CheckedChanged += new EventHandler(AutoToggled);
        box.Controls.Add(chkAuto);

        box.Controls.Add(Caption("They join as:", 12, 52, 90));
        cboFrom = new ComboBox();
        cboFrom.Location = new Point(106, 50);
        cboFrom.Size = new Size(200, 24);
        cboFrom.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Controls.Add(cboFrom);

        box.Controls.Add(Caption("Move them to:", 330, 52, 96));
        cboTo = new ComboBox();
        cboTo.Location = new Point(430, 50);
        cboTo.Size = new Size(200, 24);
        cboTo.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Controls.Add(cboTo);

        return box;
    }

    Control BuildWatchlistBox()
    {
        var box = Section("Step 4 - Specific players (the watchlist)", 0);

        chkWatchlist = new CheckBox();
        chkWatchlist.Text = "Use the watchlist";
        chkWatchlist.Location = new Point(12, 22);
        chkWatchlist.Size = new Size(160, 22);
        chkWatchlist.CheckedChanged += new EventHandler(WatchlistToggled);
        box.Controls.Add(chkWatchlist);

        var note = Caption("Not in the group yet? RankBot waits and ranks them the moment they join.",
            180, 22, 520);
        note.ForeColor = Color.DimGray;
        box.Controls.Add(note);

        grid = new DataGridView();
        grid.Location = new Point(12, 48);
        grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        grid.Size = new Size(600, 150);
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.BackgroundColor = Color.White;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.DataError += new DataGridViewDataErrorEventHandler(GridDataError);

        var playerColumn = new DataGridViewTextBoxColumn();
        playerColumn.HeaderText = "Player (username or user ID)";
        playerColumn.FillWeight = 60;
        grid.Columns.Add(playerColumn);

        rankColumn = new DataGridViewComboBoxColumn();
        rankColumn.HeaderText = "Rank";
        rankColumn.FillWeight = 40;
        rankColumn.FlatStyle = FlatStyle.Flat;
        grid.Columns.Add(rankColumn);

        box.Controls.Add(grid);

        btnAdd = new Button();
        btnAdd.Text = "Add player";
        btnAdd.Size = new Size(100, 26);
        btnAdd.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnAdd.Click += new EventHandler(AddRowClicked);
        box.Controls.Add(btnAdd);

        btnRemove = new Button();
        btnRemove.Text = "Remove";
        btnRemove.Size = new Size(100, 26);
        btnRemove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnRemove.Click += new EventHandler(RemoveRowClicked);
        box.Controls.Add(btnRemove);

        box.Resize += delegate
        {
            grid.Size = new Size(box.Width - 140, box.Height - 62);
            btnAdd.Location = new Point(box.Width - 118, 48);
            btnRemove.Location = new Point(box.Width - 118, 80);
        };

        return box;
    }

    Control BuildNeverTouchBox()
    {
        var box = Section("Step 5 - Never touch these players", 96);

        var note = Caption("One username or user ID per line. RankBot will never change their rank - "
            + "not even if they are on the watchlist.", 12, 20, 700);
        note.ForeColor = Color.DimGray;
        box.Controls.Add(note);

        txtNeverTouch = new TextBox();
        txtNeverTouch.Location = new Point(12, 42);
        txtNeverTouch.Multiline = true;
        txtNeverTouch.ScrollBars = ScrollBars.Vertical;
        txtNeverTouch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        txtNeverTouch.Size = new Size(600, 40);
        box.Controls.Add(txtNeverTouch);

        box.Resize += delegate
        {
            txtNeverTouch.Size = new Size(box.Width - 24, box.Height - 52);
        };

        return box;
    }

    Control BuildButtonRow()
    {
        var row = new FlowLayoutPanel();
        row.Dock = DockStyle.Fill;
        row.Height = 42;
        row.FlowDirection = FlowDirection.LeftToRight;
        row.Padding = new Padding(0, 6, 0, 6);

        // Widths are kept tight on purpose: all five have to fit on one line at
        // the minimum window width, or the last one gets pushed off the edge.
        btnPower = new Button();
        btnPower.Size = new Size(130, 28);
        btnPower.FlatStyle = FlatStyle.Flat;
        btnPower.Click += new EventHandler(PowerClicked);
        row.Controls.Add(btnPower);

        chkDryRun = new CheckBox();
        chkDryRun.Text = "Practice mode";
        chkDryRun.Size = new Size(110, 26);
        chkDryRun.TextAlign = ContentAlignment.MiddleLeft;
        row.Controls.Add(chkDryRun);

        btnSave = new Button();
        btnSave.Text = "Save";
        btnSave.Size = new Size(80, 28);
        btnSave.Click += new EventHandler(SaveClicked);
        row.Controls.Add(btnSave);

        btnRun = new Button();
        btnRun.Text = "Save and run now";
        btnRun.Size = new Size(130, 28);
        btnRun.Click += new EventHandler(RunClicked);
        row.Controls.Add(btnRun);

        btnFolder = new Button();
        btnFolder.Text = "Show my files";
        btnFolder.Size = new Size(110, 28);
        btnFolder.Click += new EventHandler(ShowFolderClicked);
        row.Controls.Add(btnFolder);

        btnPrepare = new Button();
        btnPrepare.Text = "Prepare GitHub files";
        btnPrepare.Size = new Size(140, 28);
        btnPrepare.Click += new EventHandler(PrepareUploadClicked);
        row.Controls.Add(btnPrepare);

        return row;
    }

    Control BuildLogBox()
    {
        txtLog = new TextBox();
        txtLog.Dock = DockStyle.Fill;
        txtLog.Multiline = true;
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.BackColor = Color.FromArgb(30, 30, 30);
        txtLog.ForeColor = Color.Gainsboro;
        txtLog.Font = new Font("Consolas", 9f);
        return txtLog;
    }

    // ========================================================================
    // Small helpers
    // ========================================================================

    void Log(string line)
    {
        if (txtLog.InvokeRequired)
        {
            txtLog.BeginInvoke(new Action<string>(Log), line);
            return;
        }
        txtLog.AppendText(line + Environment.NewLine);
    }

    void GridDataError(object sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false; // a rank that no longer exists must not crash the window
    }

    void AutoToggled(object sender, EventArgs e)
    {
        cboFrom.Enabled = chkAuto.Checked;
        cboTo.Enabled = chkAuto.Checked;
    }

    void WatchlistToggled(object sender, EventArgs e)
    {
        grid.Enabled = chkWatchlist.Checked;
        btnAdd.Enabled = chkWatchlist.Checked;
        btnRemove.Enabled = chkWatchlist.Checked;
    }

    void AddRowClicked(object sender, EventArgs e)
    {
        grid.Rows.Add();
    }

    void RemoveRowClicked(object sender, EventArgs e)
    {
        foreach (DataGridViewRow row in grid.SelectedRows)
        {
            if (!row.IsNewRow) grid.Rows.Remove(row);
        }
        if (grid.SelectedRows.Count == 0 && grid.CurrentRow != null && !grid.CurrentRow.IsNewRow)
        {
            grid.Rows.Remove(grid.CurrentRow);
        }
    }

    static string JsonString(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    // ========================================================================
    // Talking to Roblox
    // ========================================================================

    class Reply
    {
        public bool Ok;
        public int Status;
        public string Text;
        public Dictionary<string, object> Json;
    }

    static Reply Get(string url, string apiKey, string cookie)
    {
        var reply = new Reply();
        try
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.UserAgent = "RankBotSetup";
            req.Timeout = 20000;
            if (!string.IsNullOrEmpty(apiKey)) req.Headers["x-api-key"] = apiKey;
            if (!string.IsNullOrEmpty(cookie))
            {
                req.CookieContainer = new CookieContainer();
                req.CookieContainer.Add(new Cookie(".ROBLOSECURITY", cookie, "/", ".roblox.com"));
            }

            using (var res = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(res.GetResponseStream()))
            {
                reply.Status = (int)res.StatusCode;
                reply.Text = reader.ReadToEnd();
                reply.Ok = true;
            }
        }
        catch (WebException ex)
        {
            var res = ex.Response as HttpWebResponse;
            if (res != null)
            {
                reply.Status = (int)res.StatusCode;
                using (var reader = new StreamReader(res.GetResponseStream()))
                {
                    reply.Text = reader.ReadToEnd();
                }
            }
            else
            {
                reply.Text = ex.Message;
            }
        }
        catch (Exception ex)
        {
            reply.Text = ex.Message;
        }

        if (reply.Ok && !string.IsNullOrEmpty(reply.Text))
        {
            try
            {
                reply.Json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reply.Text);
            }
            catch { }
        }
        return reply;
    }

    string GroupId()
    {
        return txtGroupId.Text.Trim();
    }

    void CheckGroupClicked(object sender, EventArgs e)
    {
        long ignored;
        if (!long.TryParse(GroupId(), out ignored))
        {
            lblGroupInfo.ForeColor = Color.Firebrick;
            lblGroupInfo.Text = "That is not a group ID. It should be only numbers.";
            return;
        }

        Cursor = Cursors.WaitCursor;
        btnCheck.Enabled = false;
        try
        {
            // Both of these are public, so this works before you enter any login.
            var info = Get("https://groups.roblox.com/v1/groups/" + GroupId(), null, null);
            if (!info.Ok || info.Json == null)
            {
                lblGroupInfo.ForeColor = Color.Firebrick;
                lblGroupInfo.Text = info.Status == 400
                    ? "No group has that ID."
                    : "Could not reach Roblox (" + info.Status + ").";
                return;
            }

            var loaded = Get("https://groups.roblox.com/v1/groups/" + GroupId() + "/roles", null, null);
            if (!loaded.Ok || loaded.Json == null || !loaded.Json.ContainsKey("roles"))
            {
                lblGroupInfo.ForeColor = Color.Firebrick;
                lblGroupInfo.Text = "Found the group, but could not read its ranks.";
                return;
            }

            roles.Clear();
            // JavaScriptSerializer hands back an ArrayList here, not object[].
            var rawRoles = loaded.Json["roles"] as System.Collections.IEnumerable;
            if (rawRoles == null)
            {
                lblGroupInfo.ForeColor = Color.Firebrick;
                lblGroupInfo.Text = "Roblox sent back a rank list we could not read.";
                return;
            }
            foreach (object item in rawRoles)
            {
                var r = item as Dictionary<string, object>;
                if (r == null) continue;
                var role = new Role();
                role.Id = Convert.ToString(r["id"], CultureInfo.InvariantCulture);
                role.Name = Convert.ToString(r["name"]);
                role.Rank = Convert.ToInt32(r["rank"]);
                roles.Add(role);
            }

            lblGroupInfo.ForeColor = Color.SeaGreen;
            lblGroupInfo.Text = Convert.ToString(info.Json["name"]) + "  -  " + roles.Count + " ranks";
            Log("Group found: " + Convert.ToString(info.Json["name"]));

            WarnAboutDuplicateRanks();
            FillRankPickers();
        }
        finally
        {
            btnCheck.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    void TestKeyClicked(object sender, EventArgs e)
    {
        string key = txtApiKey.Text.Trim();
        if (key.Length == 0)
        {
            lblKeyStatus.ForeColor = Color.Firebrick;
            lblKeyStatus.Text = "empty";
            return;
        }
        long ignored;
        if (!long.TryParse(GroupId(), out ignored))
        {
            lblKeyStatus.ForeColor = Color.Firebrick;
            lblKeyStatus.Text = "set group ID first";
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            var reply = Get("https://apis.roblox.com/cloud/v2/groups/" + GroupId(), key, null);
            if (reply.Ok)
            {
                lblKeyStatus.ForeColor = Color.SeaGreen;
                lblKeyStatus.Text = "works";
                Log("API key works for this group.");
            }
            else
            {
                lblKeyStatus.ForeColor = Color.Firebrick;
                lblKeyStatus.Text = "failed (" + reply.Status + ")";
                Log("API key failed: HTTP " + reply.Status + " " + reply.Text);
                if (reply.Status == 401 || reply.Status == 403)
                {
                    Log("  -> Check the key has group:read AND group:write for THIS group,");
                    Log("     and that its allowed IP list is 0.0.0.0/0.");
                }
            }
        }
        finally { Cursor = Cursors.Default; }
    }

    void TestCookieClicked(object sender, EventArgs e)
    {
        string cookie = txtCookie.Text.Trim();
        if (cookie.Length == 0)
        {
            lblCookieStatus.ForeColor = Color.Firebrick;
            lblCookieStatus.Text = "empty";
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            var reply = Get("https://users.roblox.com/v1/users/authenticated", null, cookie);
            if (reply.Ok && reply.Json != null && reply.Json.ContainsKey("name"))
            {
                lblCookieStatus.ForeColor = Color.SeaGreen;
                lblCookieStatus.Text = Convert.ToString(reply.Json["name"]);
                Log("Cookie works - logged in as " + Convert.ToString(reply.Json["name"]) + ".");
            }
            else
            {
                lblCookieStatus.ForeColor = Color.Firebrick;
                lblCookieStatus.Text = "failed (" + reply.Status + ")";
                Log("Cookie failed: HTTP " + reply.Status + ". Cookies stop working when you log out of Roblox.");
            }
        }
        finally { Cursor = Cursors.Default; }
    }

    // ========================================================================
    // Filling the rank pickers once the ranks are known
    // ========================================================================

    // Roblox lets two roles share the same rank number. Settings are stored by
    // rank number, so if that happens RankBot always picks the first one.
    void WarnAboutDuplicateRanks()
    {
        var seen = new Dictionary<int, string>();
        foreach (Role role in roles)
        {
            if (seen.ContainsKey(role.Rank))
            {
                Log("Heads up: \"" + seen[role.Rank] + "\" and \"" + role.Name
                    + "\" are both rank " + role.Rank + ". RankBot will use \""
                    + seen[role.Rank] + "\". Give them different rank numbers in Roblox to be sure.");
            }
            else
            {
                seen[role.Rank] = role.Name;
            }
        }
    }

    void FillRankPickers()
    {
        object keepFrom = cboFrom.SelectedItem;
        object keepTo = cboTo.SelectedItem;

        cboFrom.Items.Clear();
        cboTo.Items.Clear();
        rankColumn.Items.Clear();

        foreach (Role role in roles)
        {
            cboFrom.Items.Add(role);
            cboTo.Items.Add(role);
            // Two roles can look identical if they share a name and rank;
            // a duplicate entry in the dropdown would just be confusing.
            if (!rankColumn.Items.Contains(role.ToString())) rankColumn.Items.Add(role.ToString());
        }

        if (keepFrom != null) SelectRole(cboFrom, (Role)keepFrom);
        else if (cboFrom.Items.Count > 0) cboFrom.SelectedIndex = 0;

        if (keepTo != null) SelectRole(cboTo, (Role)keepTo);

        // Rank names in the grid may be stale after ranks are renamed; make
        // sure every value still in a cell is a legal choice.
        foreach (DataGridViewRow row in grid.Rows)
        {
            var cell = row.Cells[1];
            if (cell.Value == null) continue;
            string text = Convert.ToString(cell.Value);
            if (!rankColumn.Items.Contains(text))
            {
                Role match = RoleFromStoredValue(text);
                cell.Value = match != null ? match.ToString() : null;
            }
        }
    }

    // Matched on name+rank, not by reference: pressing Check builds brand new
    // Role objects, so the old selection is never the same instance.
    static void SelectRole(ComboBox box, Role role)
    {
        if (role == null) return;
        for (int i = 0; i < box.Items.Count; i++)
        {
            Role item = (Role)box.Items[i];
            if (item.Rank == role.Rank && item.Name == role.Name) { box.SelectedIndex = i; return; }
        }
    }

    // Roblox allows two roles to share a rank number. When that happens the
    // number alone is ambiguous, so the rank NAME is saved instead.
    bool RankNumberIsAmbiguous(int rank)
    {
        int count = 0;
        foreach (Role role in roles) { if (role.Rank == rank) count++; }
        return count > 1;
    }

    string RankValueJson(Role role)
    {
        if (RankNumberIsAmbiguous(role.Rank)) return JsonString(role.Name);
        return role.Rank.ToString(CultureInfo.InvariantCulture);
    }

    // Matches "Member  (rank 10)", or a bare rank number, or a bare rank name.
    Role RoleFromStoredValue(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        foreach (Role role in roles)
        {
            if (role.ToString() == stored) return role;
        }
        int rank;
        if (int.TryParse(stored, out rank))
        {
            foreach (Role role in roles) { if (role.Rank == rank) return role; }
        }
        foreach (Role role in roles)
        {
            if (string.Equals(role.Name, stored, StringComparison.OrdinalIgnoreCase)) return role;
        }
        return null;
    }

    // ========================================================================
    // Loading and saving
    // ========================================================================

    void LoadEverything()
    {
        txtApiKey.Text = ReadTextFile("apikey.txt");
        txtCookie.Text = ReadTextFile("cookie.txt");

        Dictionary<string, object> config = null;
        try
        {
            if (File.Exists(P("config.json")))
            {
                config = new JavaScriptSerializer()
                    .Deserialize<Dictionary<string, object>>(File.ReadAllText(P("config.json")));
            }
        }
        catch (Exception ex) { Log("config.json could not be read: " + ex.Message); }

        // Kept as text, because a rank can be saved as a number OR a name.
        string savedFrom = "", savedTo = "";
        chkAuto.Checked = true;
        chkWatchlist.Checked = true;
        chkDryRun.Checked = true;

        if (config != null)
        {
            if (config.ContainsKey("groupId") && Convert.ToInt64(config["groupId"]) > 0)
            {
                txtGroupId.Text = Convert.ToString(config["groupId"], CultureInfo.InvariantCulture);
            }
            if (config.ContainsKey("active")) active = Convert.ToBoolean(config["active"]);
            if (config.ContainsKey("watchlistEnabled")) chkWatchlist.Checked = Convert.ToBoolean(config["watchlistEnabled"]);
            if (config.ContainsKey("dryRun")) chkDryRun.Checked = Convert.ToBoolean(config["dryRun"]);

            if (config.ContainsKey("autoRankNewMembers"))
            {
                var auto = config["autoRankNewMembers"] as Dictionary<string, object>;
                if (auto != null)
                {
                    if (auto.ContainsKey("enabled")) chkAuto.Checked = Convert.ToBoolean(auto["enabled"]);
                    if (auto.ContainsKey("fromRank")) savedFrom = Convert.ToString(auto["fromRank"]);
                    if (auto.ContainsKey("toRank")) savedTo = Convert.ToString(auto["toRank"]);
                }
            }
        }

        LoadWatchlistRows();
        LoadNeverTouch();
        ShowPowerState();
        AutoToggled(null, null);
        WatchlistToggled(null, null);

        if (txtGroupId.Text.Length > 0)
        {
            CheckGroupClicked(null, null);
            if (roles.Count > 0)
            {
                SelectRole(cboFrom, RoleFromStoredValue(savedFrom));
                SelectRole(cboTo, RoleFromStoredValue(savedTo));
            }
        }
    }

    void LoadWatchlistRows()
    {
        grid.Rows.Clear();
        try
        {
            if (!File.Exists(P("watchlist.json"))) return;
            var list = new JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(P("watchlist.json"))) as System.Collections.IEnumerable;
            if (list == null) return;
            foreach (object item in list)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null) continue;
                string player = entry.ContainsKey("username")
                    ? Convert.ToString(entry["username"])
                    : (entry.ContainsKey("userId") ? Convert.ToString(entry["userId"]) : "");
                string rank = entry.ContainsKey("rank") ? Convert.ToString(entry["rank"]) : "";
                grid.Rows.Add(player, rank);
            }
        }
        catch (Exception ex) { Log("watchlist.json could not be read: " + ex.Message); }
    }

    static string ReadTextFile(string name)
    {
        try
        {
            return File.Exists(P(name)) ? File.ReadAllText(P(name)).Trim() : "";
        }
        catch { return ""; }
    }

    bool SaveEverything()
    {
        long groupId;
        if (!long.TryParse(GroupId(), out groupId) || groupId <= 0)
        {
            MessageBox.Show(this, "Put your group ID in first, then press Check group.",
                "Missing group ID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (txtApiKey.Text.Trim().Length == 0 && txtCookie.Text.Trim().Length == 0)
        {
            MessageBox.Show(this, "RankBot needs either an API key or a cookie to log in.",
                "No login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (chkAuto.Checked && (cboFrom.SelectedItem == null || cboTo.SelectedItem == null))
        {
            MessageBox.Show(this, "Pick both ranks for auto-ranking, or untick it.",
                "Ranks not chosen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (chkAuto.Checked && ((Role)cboFrom.SelectedItem).Rank == ((Role)cboTo.SelectedItem).Rank)
        {
            MessageBox.Show(this, "The joining rank and the target rank are the same, so nothing would happen.",
                "Same rank twice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            string fromRank = chkAuto.Checked ? RankValueJson((Role)cboFrom.SelectedItem) : "0";
            string toRank = chkAuto.Checked ? RankValueJson((Role)cboTo.SelectedItem) : "1";

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"active\": " + (active ? "true" : "false") + ",");
            json.AppendLine("  \"groupId\": " + groupId + ",");
            json.AppendLine("  \"autoRankNewMembers\": {");
            json.AppendLine("    \"enabled\": " + (chkAuto.Checked ? "true" : "false") + ",");
            json.AppendLine("    \"fromRank\": " + fromRank + ",");
            json.AppendLine("    \"toRank\": " + toRank);
            json.AppendLine("  },");
            json.AppendLine("  \"watchlistEnabled\": " + (chkWatchlist.Checked ? "true" : "false") + ",");
            json.AppendLine("  \"maxMemberPagesToScan\": 20,");
            json.AppendLine("  \"dryRun\": " + (chkDryRun.Checked ? "true" : "false"));
            json.AppendLine("}");
            File.WriteAllText(P("config.json"), json.ToString());

            File.WriteAllText(P("watchlist.json"), BuildWatchlistJson());
            File.WriteAllText(P("nevertouch.json"), BuildNeverTouchJson());

            WriteSecret("apikey.txt", txtApiKey.Text.Trim());
            WriteSecret("cookie.txt", txtCookie.Text.Trim());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save: " + ex.Message, "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        Log("Saved.");
        return true;
    }

    string BuildWatchlistJson()
    {
        var entries = new List<string>();
        int skipped = 0;

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            string player = Convert.ToString(row.Cells[0].Value);
            string rankText = Convert.ToString(row.Cells[1].Value);
            if (player == null) player = "";
            player = player.Trim();

            if (player.Length == 0) continue;

            Role role = RoleFromStoredValue(rankText);
            if (role == null) { skipped++; continue; }

            long userId;
            string who = long.TryParse(player, out userId)
                ? "\"userId\": " + userId
                : "\"username\": " + JsonString(player);

            entries.Add("  { " + who + ", \"rank\": " + RankValueJson(role) + " }");
        }

        if (skipped > 0)
        {
            Log("Skipped " + skipped + " watchlist row(s) with no rank chosen.");
        }

        if (entries.Count == 0) return "[]" + Environment.NewLine;
        return "[" + Environment.NewLine + string.Join("," + Environment.NewLine, entries.ToArray())
            + Environment.NewLine + "]" + Environment.NewLine;
    }

    string BuildNeverTouchJson()
    {
        var entries = new List<string>();
        foreach (string line in txtNeverTouch.Lines)
        {
            string player = (line == null) ? "" : line.Trim();
            if (player.Length == 0) continue;

            long userId;
            // Numbers go in as numbers, names as text. RankBot reads both.
            entries.Add(long.TryParse(player, out userId)
                ? userId.ToString(CultureInfo.InvariantCulture)
                : JsonString(player));
        }

        if (entries.Count == 0) return "[]" + Environment.NewLine;
        return "[" + Environment.NewLine + "  "
            + string.Join("," + Environment.NewLine + "  ", entries.ToArray())
            + Environment.NewLine + "]" + Environment.NewLine;
    }

    void LoadNeverTouch()
    {
        try
        {
            if (!File.Exists(P("nevertouch.json"))) return;
            var list = new JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(P("nevertouch.json"))) as System.Collections.IEnumerable;
            if (list == null) return;

            var lines = new List<string>();
            foreach (object item in list)
            {
                string text = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(text)) lines.Add(text.Trim());
            }
            txtNeverTouch.Lines = lines.ToArray();
        }
        catch (Exception ex) { Log("nevertouch.json could not be read: " + ex.Message); }
    }

    // An empty box means "delete the file", so an old secret is never left behind.
    static void WriteSecret(string name, string value)
    {
        if (value.Length == 0)
        {
            if (File.Exists(P(name))) File.Delete(P(name));
            return;
        }
        File.WriteAllText(P(name), value);
    }

    void SaveClicked(object sender, EventArgs e)
    {
        SaveEverything();
    }

    // The 24/7 switch. Off leaves the GitHub timer running but makes every run
    // do nothing, so it can be flipped back on whenever you like.
    void PowerClicked(object sender, EventArgs e)
    {
        active = !active;
        ShowPowerState();
        Log(active ? "Switched ON." : "Switched OFF.");
        if (SaveEverything())
        {
            Log("Already put RankBot on GitHub? Then upload config.json there too,");
            Log("so the online copy matches. Press \"Show my files\" to find it.");
        }
    }

    // Builds a copy of the project with the secret files left out, so the whole
    // folder can be dragged onto GitHub without having to remember what to skip.
    void PrepareUploadClicked(object sender, EventArgs e)
    {
        if (!SaveEverything()) return;

        string target = P(UploadFolder);
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyTree(Root.TrimEnd('\\'), target);
        }
        catch (Exception ex)
        {
            Log("Could not prepare the folder: " + ex.Message);
            return;
        }

        Log("");
        Log("Ready. Everything safe to upload is in this folder:");
        Log("  " + target);
        Log("Your API key and cookie were LEFT OUT on purpose - they go in as");
        Log("GitHub Secrets instead (ROBLOX_API_KEY and ROBLOX_COOKIE).");
        Log("Drag everything inside that folder onto GitHub.");

        try { System.Diagnostics.Process.Start("explorer.exe", "\"" + target + "\""); }
        catch (Exception ex) { Log("Could not open the folder: " + ex.Message); }
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (string file in Directory.GetFiles(from))
        {
            string name = Path.GetFileName(file);
            bool banned = false;
            foreach (string skip in NeverUpload)
            {
                if (string.Equals(name, skip, StringComparison.OrdinalIgnoreCase)) banned = true;
            }
            if (banned) continue;
            File.Copy(file, Path.Combine(to, name), true);
        }

        foreach (string dir in Directory.GetDirectories(from))
        {
            string name = Path.GetFileName(dir);
            // Never copy the output folder into itself.
            if (string.Equals(name, UploadFolder, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase)) continue;
            CopyTree(dir, Path.Combine(to, name));
        }
    }

    // Opens the folder in File Explorer with config.json already picked out,
    // so it is easy to drag onto GitHub.
    void ShowFolderClicked(object sender, EventArgs e)
    {
        Log("Your files are in:");
        Log("  " + Root);
        try
        {
            if (File.Exists(P("config.json")))
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + P("config.json") + "\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + Root.TrimEnd('\\') + "\"");
            }
        }
        catch (Exception ex)
        {
            Log("Could not open the folder: " + ex.Message);
        }
    }

    void ShowPowerState()
    {
        if (active)
        {
            btnPower.Text = "24/7 mode: ON";
            btnPower.BackColor = Color.FromArgb(214, 245, 214);
            btnPower.ForeColor = Color.DarkGreen;
        }
        else
        {
            btnPower.Text = "24/7 mode: OFF";
            btnPower.BackColor = Color.FromArgb(250, 220, 220);
            btnPower.ForeColor = Color.Firebrick;
        }
    }

    // ========================================================================
    // Running the bot
    // ========================================================================

    void RunClicked(object sender, EventArgs e)
    {
        if (!SaveEverything()) return;

        string script = P(Path.Combine("src", "rankbot.js"));
        if (!File.Exists(script))
        {
            MessageBox.Show(this, "src\\rankbot.js is missing. Keep RankBotSetup.exe in the RankBot folder.",
                "File missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        btnRun.Enabled = false;
        txtLog.Clear();
        Log("Running RankBot...");
        Log("");

        var worker = new Thread(new ThreadStart(delegate
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("node", "\"" + script + "\"");
                psi.WorkingDirectory = Root;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    proc.OutputDataReceived += delegate(object s2, System.Diagnostics.DataReceivedEventArgs a)
                    {
                        if (a.Data != null) Log(a.Data);
                    };
                    proc.ErrorDataReceived += delegate(object s2, System.Diagnostics.DataReceivedEventArgs a)
                    {
                        if (a.Data != null) Log(a.Data);
                    };
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Log("");
                Log("Node.js is not installed on this computer.");
                Log("Get it from https://nodejs.org (pick the LTS button), then try again.");
                Log("You only need Node here for testing - GitHub runs it for you online.");
            }
            catch (Exception ex)
            {
                Log("Could not run RankBot: " + ex.Message);
            }
            finally
            {
                btnRun.BeginInvoke(new Action(delegate { btnRun.Enabled = true; }));
            }
        }));
        worker.IsBackground = true;
        worker.Start();
    }

    // RankBotSetup.exe --prepare  does the "Prepare GitHub files" job without
    // opening the window, so it can be scripted or tested.
    static bool PrepareHeadless()
    {
        try
        {
            string target = P(UploadFolder);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyTree(Root.TrimEnd('\\'), target);
            return true;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(P("crash.txt"), ex.ToString()); }
            catch { }
            return false;
        }
    }

    [STAThread]
    public static void Main()
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (string.Equals(arg, "--prepare", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = PrepareHeadless() ? 0 : 1;
                return;
            }
        }

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
        catch (Exception ex)
        {
            // Without this a crash just closes the window with no explanation.
            string details;
            try { details = ex.ToString(); }
            catch { details = ex.GetType().FullName + ": " + ex.Message; }

            try { File.WriteAllText(P("crash.txt"), details); }
            catch { }

            MessageBox.Show("RankBot Setup hit a problem and had to stop.\r\n\r\n"
                + details + "\r\n\r\n(also saved to crash.txt)",
                "RankBot Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
