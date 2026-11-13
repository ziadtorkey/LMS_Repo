// Saves a stream sent from .NET as a file download (audit export).
// Shows a stream sent from .NET in an element (the protected library reader): the bearer token cannot ride on a plain src.
window.lmsBlobUrl = async function (streamReference, contentType) {
    const buffer = await streamReference.arrayBuffer();
    return URL.createObjectURL(new Blob([buffer], { type: contentType }));
};
window.lmsRevokeBlobUrl = function (url) {
    if (url) URL.revokeObjectURL(url);
};

window.lmsDownloadFile = async function (fileName, streamReference) {
    const buffer = await streamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer]));
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
};
