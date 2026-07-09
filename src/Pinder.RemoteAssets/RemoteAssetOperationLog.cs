using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Pinder.RemoteAssets.Exceptions;

namespace Pinder.RemoteAssets
{
    internal sealed class RemoteAssetOperationLog
    {
        private readonly ILogger _logger;
        private readonly string _operation;
        private readonly string? _assetId;
        private readonly string? _assetKind;
        private readonly Stopwatch _stopwatch;

        private RemoteAssetOperationLog(
            ILogger logger,
            string operation,
            string? assetId,
            string? assetKind)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            _assetId = assetId;
            _assetKind = assetKind;
            _stopwatch = Stopwatch.StartNew();
        }

        public static RemoteAssetOperationLog Begin(
            ILogger logger,
            string operation,
            string? assetId = null,
            string? assetKind = null)
        {
            var op = new RemoteAssetOperationLog(logger, operation, assetId, assetKind);
            logger.LogInformation(
                "remote_asset_operation_begin operation={RemoteAssetOperation} asset_id={AssetId} asset_kind={AssetKind}",
                operation,
                assetId,
                assetKind);
            return op;
        }

        public void Complete(
            string outcome,
            string? assetKind = null,
            int? itemCount = null,
            bool? hasNextCursor = null,
            int? statusCode = null,
            int? metadataBytes = null,
            int? payloadBytes = null)
        {
            _stopwatch.Stop();
            _logger.LogInformation(
                "remote_asset_operation_complete operation={RemoteAssetOperation} outcome={Outcome} elapsed_ms={ElapsedMs} asset_id={AssetId} asset_kind={AssetKind} item_count={ItemCount} has_next_cursor={HasNextCursor} status_code={StatusCode} metadata_bytes={MetadataBytes} payload_bytes={PayloadBytes}",
                _operation,
                outcome,
                _stopwatch.ElapsedMilliseconds,
                _assetId,
                assetKind ?? _assetKind,
                itemCount,
                hasNextCursor,
                statusCode,
                metadataBytes,
                payloadBytes);
        }

        public void Failure(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            _stopwatch.Stop();
            int? statusCode = exception is RemoteAssetException remoteAssetException
                ? remoteAssetException.StatusCode
                : (int?)null;

            _logger.LogError(
                exception,
                "remote_asset_operation_failure operation={RemoteAssetOperation} outcome={Outcome} elapsed_ms={ElapsedMs} asset_id={AssetId} asset_kind={AssetKind} status_code={StatusCode} exception_type={ExceptionType}",
                _operation,
                "failure",
                _stopwatch.ElapsedMilliseconds,
                _assetId,
                _assetKind,
                statusCode,
                exception.GetType().Name);
        }
    }
}
