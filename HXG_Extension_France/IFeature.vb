Imports ESPRIT.NetApi.Ribbon

''' <summary>
''' Contract that every extension feature must implement.
''' Each feature is responsible for its own ribbon UI, button handling, and cleanup.
''' </summary>
Public Interface IFeature

    ''' <summary>Adds the feature's ribbon controls to the extension tab.</summary>
    Sub Setup(tab As IRibbonTab)

    ''' <summary>
    ''' Handles a ribbon button click. Returns True if this feature handled the event,
    ''' which stops propagation to other features.
    ''' </summary>
    Function HandleButtonClick(e As ButtonClickEventArgs) As Boolean

    ''' <summary>Called when the extension is disconnected. Release any resources here.</summary>
    Sub Disconnect()

End Interface
