Imports NAudio.Wave

Public NotInheritable Class SoundPlayer
    Implements IDisposable

    Private ReadOnly reader As AudioFileReader
    Private ReadOnly waveOut As WaveOutEvent
    Private isLooping As Boolean = False
    Private disposedValue As Boolean

    Public Sub New(filename As String)
        reader = New AudioFileReader(filename)
        waveOut = New WaveOutEvent
        waveOut.Init(reader)

        AddHandler waveOut.PlaybackStopped, AddressOf OnPlaybackStopped
    End Sub

    Public Sub Play()
        If waveOut IsNot Nothing Then
            isLooping = False
            reader.Position = 0
            waveOut.Play()
        End If
    End Sub

    Public Sub PlayLooping()
        If waveOut IsNot Nothing Then
            isLooping = True
            reader.Position = 0
            waveOut.Play()
        End If
    End Sub

    Public Sub [Stop]()
        If waveOut IsNot Nothing Then
            isLooping = False
            waveOut.Stop()
        End If
    End Sub

    Public Sub OnPlaybackStopped(sender As Object, e As StoppedEventArgs)
        If isLooping AndAlso waveOut IsNot Nothing Then
            reader.Position = 0
            waveOut.Play()
        End If
    End Sub

    Private Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                If waveOut IsNot Nothing Then
                    RemoveHandler waveOut.PlaybackStopped, AddressOf OnPlaybackStopped
                End If
                waveOut?.Dispose()
                reader?.Dispose()
            End If

            ' TODO: free unmanaged resources (unmanaged objects) and override finalizer
            ' TODO: set large fields to null
            disposedValue = True
        End If
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        ' Do not change this code. Put cleanup code in 'Dispose(disposing As Boolean)' method
        Dispose(disposing:=True)
        GC.SuppressFinalize(Me)
    End Sub
End Class
