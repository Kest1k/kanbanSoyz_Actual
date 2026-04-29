/// <summary>
/// Вызывается при получении клиентом запроса на открытие внешней ссылки
/// </summary>
/// <param name="obj">Объект для открытия</param>
/// <param name="url">Полный URL внешней ссылки, переданной для открытия в клиенте</param>
/// <param name="openTree">true, если предполагается, что объект должен быть открыт в дереве 
/// (фактически используется true во всех случаях)</param>
/// <returns>true, если запрос обработан и другие обработчики, а так же штатное поведение, вызывать не следует</returns>
public override bool DoHandleExternalLink( ScriptingObject obj, String url, bool openTree )
{
    var io = obj as InfoObject;
    if( io == null ) return false;

    // 14068UL - тестовый сервер
    // 804663UL - основной сервер
    if( io.Id == 14068UL || io.Id == 804663UL )
    {
        OpenBoardAndRefresh( io );
        return true;
    }

    return false;
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

private void BringPlmToFront()
{
    try
    {
        foreach( System.Windows.Forms.Form form in System.Windows.Forms.Application.OpenForms )
            BringFormToFront( form );
    }
    catch { }

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

private void BringFormToFront( System.Windows.Forms.Form form )
{
    if( form == null || form.IsDisposed ) return;

    try
    {
        if( form.WindowState == System.Windows.Forms.FormWindowState.Minimized )
            form.WindowState = System.Windows.Forms.FormWindowState.Normal;

        ShowWindow( form.Handle, SW_RESTORE );
        form.Show();
        form.Activate();
        form.BringToFront();
        form.Focus();

        var wasTopMost = form.TopMost;
        form.TopMost = true;
        form.TopMost = wasTopMost;

        SetForegroundWindow( form.Handle );
    }
    catch { }
}

private const int SW_RESTORE = 9;

[System.Runtime.InteropServices.DllImport( "user32.dll" )]
private static extern bool ShowWindow( IntPtr hWnd, int nCmdShow );

[System.Runtime.InteropServices.DllImport( "user32.dll" )]
private static extern bool SetForegroundWindow( IntPtr hWnd );
