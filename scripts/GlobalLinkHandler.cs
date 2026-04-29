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


private const int SW_RESTORE = 9;

[System.Runtime.InteropServices.DllImport( "user32.dll" )]
private static extern bool ShowWindow( IntPtr hWnd, int nCmdShow );

[System.Runtime.InteropServices.DllImport( "user32.dll" )]
private static extern bool SetForegroundWindow( IntPtr hWnd );
