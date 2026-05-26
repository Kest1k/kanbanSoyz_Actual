private static System.Collections.Generic.HashSet<ulong> _notifiedItems = null;
private static readonly object _notifyLock = new object();

// Файл для персистентного dedup. Переживает рестарт клиента и cache eviction.
private static string GetNotifiedFilePath()
{
    var dir = System.IO.Path.Combine(
        System.Environment.GetFolderPath( System.Environment.SpecialFolder.LocalApplicationData ),
        "KanbanSoyz" );
    try { if( !System.IO.Directory.Exists( dir ) ) System.IO.Directory.CreateDirectory( dir ); } catch { }
    return System.IO.Path.Combine( dir, "notified.txt" );
}

// Lazy-load + автообрезка старых записей (>30 дней по mtime).
// Формат файла: <id>\t<unixTime>\n
private static System.Collections.Generic.HashSet<ulong> GetNotifiedSet()
{
    if( _notifiedItems != null ) return _notifiedItems;
    var set = new System.Collections.Generic.HashSet<ulong>();
    try
    {
        var path = GetNotifiedFilePath();
        if( System.IO.File.Exists( path ) )
        {
            var cutoff = ( DateTime.UtcNow - TimeSpan.FromDays( 30 ) ).Ticks / TimeSpan.TicksPerSecond;
            var liveLines = new System.Collections.Generic.List<string>();
            bool needsCompact = false;
            foreach( var raw in System.IO.File.ReadAllLines( path ) )
            {
                var line = raw.Trim();
                if( line.Length == 0 ) continue;
                var parts = line.Split( '\t' );
                ulong id;
                if( !ulong.TryParse( parts[0], out id ) ) continue;
                long ts = 0;
                if( parts.Length >= 2 ) long.TryParse( parts[1], out ts );
                if( ts > 0 && ts < cutoff ) { needsCompact = true; continue; }
                set.Add( id );
                liveLines.Add( line );
            }
            if( needsCompact )
            {
                try { System.IO.File.WriteAllLines( path, liveLines ); } catch { }
            }
        }
    }
    catch { }
    _notifiedItems = set;
    return set;
}

private static void PersistNotified( ulong id )
{
    try
    {
        var ts = ( DateTime.UtcNow.Ticks - new DateTime( 1970, 1, 1 ).Ticks ) / TimeSpan.TicksPerSecond;
        System.IO.File.AppendAllText( GetNotifiedFilePath(), id.ToString() + "\t" + ts.ToString() + Environment.NewLine );
    }
    catch { }
}

public override void OnUpdated( WorkItem obj, bool isFirst )
{
    if( !isFirst ) return;

    // Guard: если нет Subject – это не наш WorkItem (пустые оповещения кэша).
    var subject = "";
    try { subject = ( obj.GetValue<string>( "Subject" ) ?? "" ).Trim(); } catch { }
    if( string.IsNullOrEmpty( subject ) ) return;

    if( !obj.IsUserRecipient( Service.GetCurrentUser() )
        || Math.Abs( ( obj.DateActivated - DateTime.Now ).TotalMinutes ) >= 180.0 )
        return;

    // Primary guard: серверный флаг новизны. Если уже viewed на сервере
    // (предыдущая сессия / другой клиент уже показал popup) – не показываем.
    // Покрывает: рестарт клиента, cache eviction, параллельные сессии.
    bool isNew = true;
    try { isNew = obj.IsNewForCurrentUser; } catch { }
    if( !isNew ) return;

    // Secondary guard: persistent file dedup. Защита если MarkAsViewedByCurrentUser
    // тихо упал и серверный флаг новизны остался true.
    lock( _notifyLock )
    {
        var set = GetNotifiedSet();
        if( set.Contains( obj.Id ) ) return;
        set.Add( obj.Id );
        PersistNotified( obj.Id );
    }

    // НЕ маркируем viewed до показа – иначе IsNewForCurrentUser=false и при
    // случайном повторном OnUpdated в этом же процессе сработает фильтр выше.
    // Маркируем после закрытия формы (см. ниже).

    System.Console.Beep( 250, 500 );

    subject = obj.GetValue<string>( "Subject" ) ?? "Уведомление Kanban";
    var taskDetails = "";
    try { taskDetails = ( obj.GetValue<string>( "TaskDetails" ) ?? "" ).Trim(); }
    catch { taskDetails = ""; }
    var taskKey = (obj.Params ?? "").Trim();

    // Определяем тип уведомления по началу строки Subject
    bool isComment = subject.StartsWith("Новый комментарий", StringComparison.OrdinalIgnoreCase);
    string windowTitle = isComment ? "Новый комментарий" : "Новая задача";

    // Если это комментарий, текст уже есть в Subject. Очищаем TaskDetails, чтобы скрыть блок "Содержание"
    if (isComment)
    {
        taskDetails = ""; 
    }

    try
    {
        // Разделяем доп. инфо (приоритет)
        var mainPart  = subject;
        var extraPart = "";
        var pipeIdx   = subject.IndexOf( " | " );
        if( pipeIdx > 0 )
        {
            mainPart  = subject.Substring( 0, pipeIdx );
            extraPart = subject.Substring( pipeIdx );
        }

        // Парсим mainPart: «Новая задача «ИмяЗадачи» от Иванова С.А.»
        var rtfMain = "";
        var q1 = mainPart.IndexOf( "\u00ab" );
        var q2 = mainPart.IndexOf( "\u00bb" );
        if( q1 >= 0 && q2 > q1 )
        {
            var before   = mainPart.Substring( 0, q1 + 1 );
            var taskName = mainPart.Substring( q1 + 1, q2 - q1 - 1 );
            var after    = mainPart.Substring( q2 );

            var otIdx = after.IndexOf( " от " );
            if( otIdx >= 0 )
            {
                var middle = after.Substring( 0, otIdx + 4 );
                var sender = after.Substring( otIdx + 4 );
                rtfMain = @"\fs22 " + RtfEncode( before )
                        + @"\b " + RtfEncode( taskName ) + @"\b0 "
                        + RtfEncode( middle )
                        + @"\b " + RtfEncode( sender ) + @"\b0";
            }
            else
            {
                rtfMain = @"\fs22 " + RtfEncode( before )
                        + @"\b " + RtfEncode( taskName ) + @"\b0 "
                        + RtfEncode( after );
            }
        }
        else
        {
            rtfMain = @"\fs22\b " + RtfEncode( mainPart ) + @"\b0";
        }

        // Собираем RTF
        var sb = new System.Text.StringBuilder();
        sb.Append( @"{\rtf1\ansi\ansicpg1251\deff0" );
        sb.Append( @"{\fonttbl{\f0\fswiss\fcharset204 Segoe UI;}}" );
        sb.Append( @"{\colortbl;\red0\green102\blue204;\red120\green120\blue120;\red60\green60\blue60;}" );
        sb.Append( @"\viewkind4\uc1\pard\sa80\f0 " );
        sb.Append( rtfMain );

        if( extraPart.Length > 0 )
            sb.Append( @"\par\fs20\cf2 " + RtfEncode( extraPart ) );

        if( taskDetails.Length > 0 )
        {
            sb.Append( @"\par\par\fs22\b " );
            sb.Append( RtfEncode( "Содержание:" ) );
            sb.Append( @"\b0\par\fs20\cf3 " );
            sb.Append( RtfEncode( taskDetails ) );
        }

        sb.Append( @"\par}" );
        var rtfText = sb.ToString();

        // Замер МАКСИМАЛЬНОЙ ширины строк (без переноса)
        const int sidePadding = 18;
        const int topPadding  = 18;
        const int botPadding  = 12;
        const int buttonBar   = 58;
        const int btnWidth    = 170;
        const int btnHeight   = 32;

        int maxLineW = 0;
        using( var fBold11  = new System.Drawing.Font( "Segoe UI", 11f, System.Drawing.FontStyle.Bold ) )
        using( var fReg10   = new System.Drawing.Font( "Segoe UI", 10f, System.Drawing.FontStyle.Regular ) )
        {
            var maxSz = new System.Drawing.Size( int.MaxValue, int.MaxValue );
            var flags = System.Windows.Forms.TextFormatFlags.NoPadding;

            maxLineW = Math.Max( maxLineW, System.Windows.Forms.TextRenderer.MeasureText( mainPart, fBold11, maxSz, flags ).Width );

            if( extraPart.Length > 0 )
                maxLineW = Math.Max( maxLineW, System.Windows.Forms.TextRenderer.MeasureText( extraPart, fReg10, maxSz, flags ).Width );

            if( taskDetails.Length > 0 )
            {
                maxLineW = Math.Max( maxLineW, System.Windows.Forms.TextRenderer.MeasureText( "Содержание:", fBold11, maxSz, flags ).Width );
                foreach( var raw in taskDetails.Split( '\n' ) )
                {
                    var line = raw.TrimEnd( '\r' );
                    if( line.Length == 0 ) continue;
                    maxLineW = Math.Max( maxLineW, System.Windows.Forms.TextRenderer.MeasureText( line, fReg10, maxSz, flags ).Width );
                }
            }
        }

        int minFormWidth = Math.Max( btnWidth + 60, 300 );
        int maxFormWidth = 650;
        int formWidth    = maxLineW + sidePadding * 2 + 24;
        if( formWidth < minFormWidth ) formWidth = minFormWidth;
        if( formWidth > maxFormWidth ) formWidth = maxFormWidth;

        // Замер высоты содержимого под выбранную ширину
        int contentHeight = 60;
        using( var measurer = new System.Windows.Forms.RichTextBox() )
        {
            measurer.BorderStyle = System.Windows.Forms.BorderStyle.None;
            measurer.Multiline   = true;
            measurer.WordWrap    = true;
            measurer.ScrollBars  = System.Windows.Forms.RichTextBoxScrollBars.None;
            measurer.Font        = new System.Drawing.Font( "Segoe UI", 10f );
            measurer.Size        = new System.Drawing.Size( formWidth - sidePadding * 2, 2000 );
            measurer.ContentsResized += ( s, e ) => { contentHeight = e.NewRectangle.Height; };
            var _h = measurer.Handle;
            measurer.Rtf = rtfText;
        }

        int formHeight = contentHeight + topPadding + botPadding + buttonBar + 8;
        if( formHeight < 130 ) formHeight = 130;

        var navigateToBoard = false;

        using( var form = new System.Windows.Forms.Form() )
        {
            form.Text            = windowTitle;
            form.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            form.MaximizeBox     = false;
            form.MinimizeBox     = false;
            form.ShowInTaskbar   = false;
            form.TopMost         = true;
            form.KeyPreview      = true;
            form.StartPosition   = System.Windows.Forms.FormStartPosition.CenterScreen;
            form.ClientSize      = new System.Drawing.Size( formWidth, formHeight );
            form.Font            = new System.Drawing.Font( "Segoe UI", 9f );
            form.BackColor       = System.Drawing.Color.White;
            form.Shown          += ( s, e ) => { try { form.Activate(); form.BringToFront(); } catch {} };
            form.KeyDown        += ( s, e ) => { if( e.KeyCode == System.Windows.Forms.Keys.Escape ) form.Close(); };

            // Нижняя панель с кнопкой по центру
            var buttonPanel = new System.Windows.Forms.TableLayoutPanel();
            buttonPanel.Dock        = System.Windows.Forms.DockStyle.Bottom;
            buttonPanel.Height      = buttonBar;
            buttonPanel.ColumnCount = 1;
            buttonPanel.RowCount    = 1;
            buttonPanel.BackColor   = System.Drawing.Color.FromArgb( 247, 247, 247 );
            buttonPanel.Padding     = new System.Windows.Forms.Padding( 0 );
            
            // Тонкая линия-разделитель сверху панели кнопок
            buttonPanel.Paint += ( s, e ) =>
            {
                using( var pen = new System.Drawing.Pen( System.Drawing.Color.FromArgb( 230, 230, 230 ) ) )
                    e.Graphics.DrawLine( pen, 0, 0, buttonPanel.Width, 0 );
            };
            
            var btnOpen = new System.Windows.Forms.Button();
            btnOpen.Text         = string.IsNullOrEmpty( taskKey ) ? "Открыть доску" : "Открыть карточку";
            btnOpen.Size         = new System.Drawing.Size( btnWidth, btnHeight );
            btnOpen.Anchor       = System.Windows.Forms.AnchorStyles.None;
            btnOpen.Margin       = new System.Windows.Forms.Padding( 0 );
            btnOpen.DialogResult = System.Windows.Forms.DialogResult.None;
            btnOpen.Font         = new System.Drawing.Font( "Segoe UI", 9f, System.Drawing.FontStyle.Bold );
            btnOpen.FlatStyle    = System.Windows.Forms.FlatStyle.Flat;
            btnOpen.BackColor    = System.Drawing.Color.FromArgb( 0, 120, 215 );
            btnOpen.ForeColor    = System.Drawing.Color.White;
            btnOpen.FlatAppearance.BorderSize = 0;
            btnOpen.Cursor       = System.Windows.Forms.Cursors.Hand;
            btnOpen.UseVisualStyleBackColor = false;
            btnOpen.Click       += ( s, e ) =>
            {
                navigateToBoard = true;
                BringPlmToFront();
                form.Close();
            };
            
            buttonPanel.Controls.Add( btnOpen, 0, 0 );
            
            // Текстовая область
            var rtb = new System.Windows.Forms.RichTextBox();
            rtb.Dock        = System.Windows.Forms.DockStyle.Fill;
            rtb.ReadOnly    = true;
            rtb.BorderStyle = System.Windows.Forms.BorderStyle.None;
            rtb.BackColor   = System.Drawing.Color.White;
            rtb.TabStop     = false;
            rtb.ScrollBars  = System.Windows.Forms.RichTextBoxScrollBars.None;
            rtb.Font        = new System.Drawing.Font( "Segoe UI", 10f );
            rtb.Rtf         = rtfText;

            var textPanel = new System.Windows.Forms.Panel();
            textPanel.Dock      = System.Windows.Forms.DockStyle.Fill;
            textPanel.Padding   = new System.Windows.Forms.Padding( sidePadding, topPadding, sidePadding, botPadding );
            textPanel.BackColor = System.Drawing.Color.White;
            textPanel.Controls.Add( rtb );

            form.Controls.Add( textPanel );
            form.Controls.Add( buttonPanel );

            form.AcceptButton = btnOpen;
            form.ShowDialog();
        }

        // Сбрасываем бейдж "Новое" в папке оповещений после показа popup.
        // IsNewForCurrentUser → false на сервере → следующий OnUpdated этого
        // WorkItem (рестарт / cache eviction) скипнет первый guard.
        try { obj.MarkAsViewedByCurrentUser(); } catch
        {
            try { Service.WriteToServerLog( "KanbanExclamation", "MarkAsViewedByCurrentUser failed post-show for WorkItem " + obj.Id ); } catch { }
        }

        if( navigateToBoard )
        {
            try
            {
                // Проверяем тему письма для определения вкладки (надежно через IndexOf)
                bool isComm = subject.IndexOf("Новый комментарий", StringComparison.OrdinalIgnoreCase) >= 0;
                string targetTab = isComm ? "chat" : "main";

                OpenBoardFromNotification( taskKey, targetTab );
            }
            catch( Exception navEx )
            {
                try { Service.WriteToServerLog( "KanbanExclamation", "Navigation error: " + navEx.Message ); } catch { }
            }
        }
        return;
    }
    catch { }

    Service.UI.ShowMessage( subject );
    try { obj.MarkAsViewedByCurrentUser(); } catch { }
}

private void OpenBoardFromNotification( string taskKey, string targetTab )
{
    var boardObj = GetBoardPage();
    if( boardObj == null ) return;

    BringPlmToFront();
    try { Service.UI.DoUIEventsDispatching(); } catch { }

    var allPanels = Service.UI.GetBrowserPanelsFromAllGroups();
    var existingTab = allPanels.FirstOrDefault( p => {
        var sheet = p.TargetPanel as ProgramSoyuz.PLM.Scripting.IPropertySheetCallback;
        return sheet != null && sheet.ScriptingObject != null && sheet.ScriptingObject.Id == boardObj.Id;
    } );

    if( existingTab != null )
    {
        existingTab.Activate();
        if( string.IsNullOrEmpty( taskKey ) ) return;

        var sheet = existingTab.TargetPanel as ProgramSoyuz.PLM.Scripting.IPropertySheetCallback;
        if( sheet != null && sheet.ScriptingObject != null )
        {
            var io = sheet.ScriptingObject as InfoObject;
            if( io != null )
            {
                // Уже открытая доска могла быть устаревшей: например, задача была
                // возвращена из «Готово» другим пользователем. Поэтому сначала
                // перерисовываем доску, а карточку открываем через AutoOpenTask
                // после свежего render.
                io.PropertyBag["AutoOpenTask"] = taskKey + "|" + targetTab;
                try
                {
                    io.Invoke( "Refresh", null );
                    return;
                }
                catch( Exception refreshEx )
                {
                    try { Service.WriteToServerLog( "KanbanExclamation", "Refresh existing board failed: " + refreshEx.Message ); } catch { }

                    var web = io.PropertyBag["Viewer"] as System.Windows.Forms.WebBrowser;
                    if( web != null && web.Document != null )
                    {
                        try
                        {
                            // Fallback: если Refresh недоступен, хотя бы открыть карточку.
                            web.Document.InvokeScript( "tcmOpen", new object[] { taskKey, targetTab } );
                        }
                        catch( Exception ex )
                        {
                            Service.WriteToServerLog( "KanbanExclamation", "JS Error: " + ex.Message );
                        }
                    }
                }
            }
        }
    }
    else
    {
        // Записываем ключ и вкладку через разделитель |
        if( !string.IsNullOrEmpty( taskKey ) )
            boardObj.PropertyBag["AutoOpenTask"] = taskKey + "|" + targetTab;
        Service.UI.OpenPropertiesPane( boardObj );
    }
}

public override void ManageMailShortcuts( WorkItem obj, UserItemLink creatorLink, UserItemLink[] recipientLinks )
{
    // НЕ обнуляем recipientLinks – WorkItem должен попасть в стандартную папку
    // оповещений PLM (как было до 5 мая).
    // Штатный popup-toast подавляется через OnPostTrayNotification (KanbanTrayFilter).
    // Кастомный RTF popup показывается через OnUpdated в этом файле.
    try { obj[ "SilentMode" ] = true; } catch { }
}

private InfoObject GetBoardPage()
{
    try
    {
        var root = Service.GetDataContainer( @"APSsServiceDataRootDirectory\UI" );
        var byPath =
            Service.GetInfoObjectOrContainer( root, @"Screens\myKanbanTest" ) ??
            Service.GetInfoObjectOrContainer( root, @"Custom\Screens\myKanbanTest" ) ??
            Service.GetInfoObjectOrContainer( root, @"Screens\Доска задач" ) ??
            Service.GetInfoObjectOrContainer( root, @"Custom\Screens\Доска задач" );

        var pageByPath = byPath as InfoObject;
        if( pageByPath != null ) return pageByPath;
    }
    catch { }

    try
    {
        var page = Service.GetInfoObject( 804663UL );
        if( page != null ) return page;
    }
    catch { }

    try
    {
        var page = Service.GetInfoObject( 14068UL );
        if( page != null ) return page;
    }
    catch { }
    return null;
}

private void BringPlmToFront()
{
    var ctrl = Service.UI.SyncControl;
    if (ctrl != null && ctrl.InvokeRequired)
    {
        ctrl.BeginInvoke((Action)BringPlmToFrontInternal);
        return;
    }
    BringPlmToFrontInternal();
}

private void BringPlmToFrontInternal()
{
    var pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

    try
    {
        using (var invisibleForm = new System.Windows.Forms.Form())
        {
            invisibleForm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            invisibleForm.ShowInTaskbar = false;
            invisibleForm.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            invisibleForm.Location = new System.Drawing.Point(-20000, -20000);
            invisibleForm.Size = new System.Drawing.Size(1, 1);
            invisibleForm.Opacity = 0;
            invisibleForm.Shown += (s, e) => invisibleForm.Close();
            invisibleForm.ShowDialog();
        }
    }
    catch { }

    IntPtr targetHandle = IntPtr.Zero;

    if (Service.UI.MainWindow != null)
        targetHandle = Service.UI.MainWindow.Handle;

    if (targetHandle == IntPtr.Zero)
    {
        try { targetHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; } catch { }
    }

    if (targetHandle == IntPtr.Zero)
    {
        EnumWindows((hwnd, lParam) =>
        {
            GetWindowThreadProcessId(hwnd, out uint wPid);
            if (wPid == pid)
            {
                var sb = new System.Text.StringBuilder(512);
                GetWindowText(hwnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (title.Contains("Союз-PLM") || title.Contains("Sojuz-PLM") || title.Contains("PLM"))
                {
                    targetHandle = hwnd;
                    return false; 
                }
            }
            return true; 
        }, IntPtr.Zero);
    }

    if (targetHandle != IntPtr.Zero)
    {
        try
        {
            var mainForm = System.Windows.Forms.Control.FromHandle(targetHandle) as System.Windows.Forms.Form;
            if (mainForm != null)
            {
                Action restoreNetProps = () => {
                    if (!mainForm.Visible) mainForm.Visible = true;
                    if (!mainForm.ShowInTaskbar) mainForm.ShowInTaskbar = true;
                    if (mainForm.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                        mainForm.WindowState = System.Windows.Forms.FormWindowState.Normal;
                };

                if (mainForm.InvokeRequired) mainForm.Invoke(restoreNetProps);
                else restoreNetProps();
            }
        }
        catch { }

        ShowWindow(targetHandle, 9); // SW_RESTORE жестко заменен на 9
        SetForegroundWindow(targetHandle);
    }
}


private string RtfEncode( string text )
{
    if( string.IsNullOrEmpty( text ) ) return "";
    var sb = new System.Text.StringBuilder();
    foreach( var c in text )
    {
        if( c > 127 )
            sb.Append( "\\u" ).Append( (int)c ).Append( "?" );
        else if( c == '\\' || c == '{' || c == '}' )
            sb.Append( '\\' ).Append( c );
        else
            sb.Append( c );
    }
    return sb.ToString();
}

[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern bool SetForegroundWindow(IntPtr hWnd);

[System.Runtime.InteropServices.DllImport("user32.dll")]
private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

[System.Runtime.InteropServices.DllImport("user32.dll")]
[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
