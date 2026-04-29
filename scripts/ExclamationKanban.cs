public override void OnUpdated( WorkItem obj, bool isFirst )
{
    if( !isFirst ) return;

    // Guard: если нет Subject — это не наш WorkItem (пустые оповещения кэша).
    var subject = "";
    try { subject = ( obj.GetValue<string>( "Subject" ) ?? "" ).Trim(); } catch { }
    if( string.IsNullOrEmpty( subject ) ) return;

    // Kanban-нагрузка заранее помечается просмотренной на сервере,
    // чтобы штатный Exclamation не попал в PLM-почту/оповещения.
    if( !obj.IsUserRecipient( Service.GetCurrentUser() )
        || Math.Abs( ( obj.DateActivated - DateTime.Now ).TotalMinutes ) >= 180.0 )
        return;

    // Дублируем отметку на клиенте, чтобы уведомление не оставалось новым
    // при любых расхождениях серверного/клиентского кэша.
    try { obj.MarkAsViewedByCurrentUser(); } catch { }

    System.Console.Beep( 250, 500 );

    subject = obj.GetValue<string>( "Subject" ) ?? "Новая задача";
    var taskDetails = "";
    try { taskDetails = ( obj.GetValue<string>( "TaskDetails" ) ?? "" ).Trim(); }
    catch { taskDetails = ""; }

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

        // ── Замер МАКСИМАЛЬНОЙ ширины строк (без переноса) ───────────
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

        // ── Замер высоты содержимого под выбранную ширину ────────────
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
            form.Text            = "Новая задача";
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

            // ── Нижняя панель с кнопкой по центру ─────────────────────
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
            btnOpen.Text         = "Открыть доску";
            btnOpen.Size         = new System.Drawing.Size( btnWidth, btnHeight );
            btnOpen.Anchor       = System.Windows.Forms.AnchorStyles.None;   // ← ключевое: центр в ячейке TLP
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
                form.Close();
            };
            
            buttonPanel.Controls.Add( btnOpen, 0, 0 );
            // ── Текстовая область ─────────────────────────────────────
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

        try { obj.MarkAsViewedByCurrentUser(); } catch { }

        if( navigateToBoard )
        {
            OpenBoardFromNotification();
        }
        return;
    }
    catch { }

    Service.UI.ShowMessage( subject );
    try { obj.MarkAsViewedByCurrentUser(); } catch { }
}

private void OpenBoardFromNotification()
{
    var page = GetBoardPage();
    if( page == null ) return;

    try
    {
        var ctrl = Service.UI.SyncControl;
        if( ctrl != null )
        {
            ctrl.BeginInvoke( (Action)( () =>
            {
                OpenBoardAndRefresh( page );
                ScheduleBringPlmToFront( 250 );
            } ) );
            return;
        }
    }
    catch { }

    OpenBoardAndRefresh( page );
    ScheduleBringPlmToFront( 250 );
}

public override void ManageMailShortcuts( WorkItem obj, UserItemLink creatorLink, UserItemLink[] recipientLinks )
{
    try { obj[ "SilentMode" ] = true; } catch { }

    try
    {
        if( recipientLinks == null ) return;

        for( int i = 0; i < recipientLinks.Length; i++ )
        {
            var link = recipientLinks[i];
            if( link == null || link.User == null ) continue;
            try { obj.MarkAsViewedBy( link.User ); } catch { }

            // Отменяем штатный ярлык во входящих уведомлениях PLM.
            // WorkItem остаётся нагрузкой получателя, поэтому OnUpdated всё равно покажет наше окно.
            recipientLinks[i] = null;
        }
    }
    catch { }
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
    try
    {
        var ctrl = Service.UI.SyncControl;
        if (ctrl != null && ctrl.InvokeRequired)
        {
            ctrl.Invoke((Action)(BringPlmToFrontInternal));
            return;
        }
        BringPlmToFrontInternal();
    }
    catch { }
}

private void BringPlmToFrontInternal()
{
    try
    {
        var mainForm = Service.UI.MainWindow as System.Windows.Forms.Form;

        string diag = "MainWindow: " + (mainForm == null ? "NULL" : ("[" + mainForm.Text + "] Vis:" + mainForm.Visible)) + "\nOpenForms:\n";
        foreach(System.Windows.Forms.Form f in System.Windows.Forms.Application.OpenForms)
        {
            if (f != null)
                diag += "- [" + f.Text + "] Vis:" + f.Visible + " Taskbar:" + f.ShowInTaskbar + "\n";
        }

        try { Service.UI.ShowMessage(diag); } catch {}

        if (mainForm == null && System.Windows.Forms.Application.OpenForms.Count > 0)
        {
            mainForm = System.Windows.Forms.Application.OpenForms[0];
        }

        if (mainForm != null)
        {
            if (!mainForm.Visible) mainForm.Visible = true;
            if (!mainForm.ShowInTaskbar) mainForm.ShowInTaskbar = true;
            if (mainForm.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                mainForm.WindowState = System.Windows.Forms.FormWindowState.Normal;

            ShowWindow(mainForm.Handle, SW_RESTORE);
            mainForm.Show();
            mainForm.Activate();
            mainForm.BringToFront();
            SetForegroundWindow(mainForm.Handle);
        }


    }
    catch (Exception ex)
    {
        try { Service.HandleException(ex, "Ошибка при восстановлении главного окна PLM"); } catch { }
    }

    try
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        process.Refresh();
        var hWnd = process.MainWindowHandle;
        if( hWnd != IntPtr.Zero )
        {
            ShowWindow( hWnd, SW_RESTORE );
            SetForegroundWindow( hWnd );
        }
    }
    catch { }
}

private void ScheduleBringPlmToFront( int interval )
{
    try
    {
        var timer = new System.Windows.Forms.Timer();
        timer.Interval = interval;
        timer.Tick += ( s, e ) =>
        {
            timer.Stop();
            timer.Dispose();
            BringPlmToFront();
        };
        timer.Start();
    }
    catch { }
}

private void OpenBoardAndRefresh( InfoObject page )
{
    BringPlmToFront();
    try { Service.UI.DoUIEventsDispatching(); } catch { }

    try { Service.UI.OpenPropertiesPane( page ); } catch { }
    try { Service.UI.DoUIEventsDispatching(); } catch { }

    BringPlmToFront();
    RefreshBoardWithDelay( page );
}

private void RefreshBoardWithDelay( InfoObject page )
{
    try
    {
        var ctrl = Service.UI.SyncControl;
        if( ctrl == null )
        {
            RefreshBoardNow( page );
            return;
        }

        ctrl.BeginInvoke( (Action)( () =>
        {
            BringPlmToFront();
            RefreshBoardNow( page );

            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 900;
            timer.Tick += ( s, e ) =>
            {
                timer.Stop();
                timer.Dispose();
                RefreshBoardNow( page );
            };
            timer.Start();
        } ) );
    }
    catch
    {
        RefreshBoardNow( page );
    }
}

private void RefreshBoardNow( InfoObject page )
{
    try { Service.UI.LoadChangesFromServer( true ); } catch { }
    try { Service.UI.DoUIEventsDispatching(); } catch { }
    try { page.Invoke( "RefreshBoard", null ); } catch { }
    try { page.Invoke( "Refresh", null ); } catch { }
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


private const int SW_RESTORE = 9;

[System.Runtime.InteropServices.DllImport( "user32.dll" )]
private static extern bool ShowWindow( IntPtr hWnd, int nCmdShow );

[System.Runtime.InteropServices.DllImport( "user32.dll" )]
private static extern bool SetForegroundWindow( IntPtr hWnd );
