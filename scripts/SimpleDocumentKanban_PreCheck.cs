// =========================================================================
//  Скрипт для шаблона InfoObjects\BASIC\Documents\SimpleDocumentKanban.
//  Задача простая: вложенный файл видят только те, кто видит задачу-владельца.
//  Все роли и иерархия остаются на стороне самой задачи: assignee, creator,
//  начальник сектора, начальник отдела, приватность и прочее.
//
//  Куда вставлять:
//      Конфигуратор → ветка "Шаблоны → Информационные объекты"
//          → InfoObjects\BASIC\Documents\SimpleDocumentKanban
//          → вкладка "Скрипты"
//      Скопировать тело метода PreCheckOperation целиком.
//
//  Что нужно на шаблоне:
//      Атрибут "OwnerTaskKey" (строка). Заводится один раз вручную в
//      конфигураторе, дальше канбан заполняет его сам при создании документа
//      (см. DoAttachLocalFile в SOYUZ_UPLOAD_KanbanScreen_script.cs).
//
//  Как работает:
//      • Не для операций видимости (View/Load/Read) - return null (стандарт)
//      • Админам - return null
//      • Если OwnerTaskKey не задан (legacy объекты) - return null
//      • Если задача не найдена - return false (документ-сирота скрыт)
//      • Если пользователь видит задачу (CheckOperation ViewObject) - return null
//      • Иначе - return false
//
//  Главное: не дублируем правила доступа. Просто спрашиваем задачу,
//  можно ли её показывать этому пользователю.
//
//  По нагрузке здесь один SearchOperation по NameKey задачи и один
//  user.CheckOperation. Оба вызова лёгкие и индексированные.
// =========================================================================

public override bool? PreCheckOperation(
    User                user,
    InfoObject          obj,
    AttributableObject  target,
    OperationIdentifier op,
    String              attrNameKey,
    CollectionElement   element )
{
    try
    {
        // Системные и анонимные сессии пусть идут штатным путём
        if( user == null ) return null;

        // Нас интересует только видимость объекта/файла
        if( op != OperationIdentifier.LoadObjectToCache
         && op != OperationIdentifier.ViewObject
         && op != OperationIdentifier.ReadFile )
            return null;

        // Админам не мешаем
        try { if( user.IsAdministrator ) return null; } catch { }

        // Ключ задачи-владельца
        string ownerKey = null;
        try { ownerKey = obj.GetString( "OwnerTaskKey" ); } catch { }
        if( string.IsNullOrEmpty( ownerKey ) ) return null; // legacy без привязки

        // Ищем задачу по NameKey
        InfoObject task = null;
        try
        {
            var searchOp = new SearchOperation( EntityIdentifier.InfoObject, "KbSdkAcl" );
            var filter   = new SearchInfoObjectFilterItem();
            filter.PropertyId             = SearchConditionFilterItem.SystemPropertyId.NameKey;
            filter.OperatorId             = RelationalOperator.Equal;
            filter.Argument1              = ownerKey;
            searchOp.SearchExpressionTree = filter;
            searchOp.MaxSQLResultsCount   = 3;
            searchOp.MaxResultsCount      = 3;
            searchOp.Execute( false );

            if( searchOp.FoundObjects != null )
            {
                foreach( var o in searchOp.FoundObjects.OfType<InfoObject>() )
                {
                    var k = string.IsNullOrEmpty( o.NameKey ) ? o.Id.ToString() : o.NameKey;
                    if( k == ownerKey ) { task = o; break; }
                }
            }
        }
        catch { }
        if( task == null ) return false; // задача удалена - документ-сирота скрыт

        // Если задача видна пользователю, документ тоже показываем.
        // Подробности доступа уже внутри задачи: assignee/creator/иерархия/роль.
        try
        {
            if( user.CheckOperation( task, OperationIdentifier.ViewObject, null ) )
                return null;
        }
        catch { return null; } // не смогли проверить - не блокируем

        // Задачу не видит - документ тоже прячем
        return false;
    }
    catch
    {
        // При неожиданной ошибке не валим весь UI, а отдаём управление штатной схеме
        return null;
    }
}
