function formatBytes(byteCount) {
	if (!Number.isFinite(byteCount) || byteCount <= 0) {
		return '0 KB';
	}

	const units = ['B', 'KB', 'MB', 'GB'];
	const exponent = Math.min(Math.floor(Math.log(byteCount) / Math.log(1024)), units.length - 1);
	const value = byteCount / (1024 ** exponent);
	return `${value.toFixed(exponent === 0 ? 0 : 1)} ${units[exponent]}`;
}

function setUploadBusyState(isBusy) {
	const buttonText = document.getElementById('uploadButtonText');
	const spinner = document.getElementById('uploadSpinner');
	const submitButton = document.getElementById('uploadSubmitButton');
	const fileInput = document.getElementById('fileInput');

	if (!buttonText || !spinner || !submitButton || !fileInput) {
		return;
	}

	buttonText.hidden = isBusy;
	spinner.hidden = !isBusy;
	submitButton.disabled = isBusy;
	fileInput.disabled = isBusy;
}

function updateUploadProgress(percentage, label, isProcessing) {
	const progressCard = document.getElementById('uploadProgress');
	const progressBar = document.getElementById('uploadProgressBar');
	const progressValue = document.getElementById('uploadProgressValue');
	const progressLabel = document.getElementById('uploadProgressLabel');

	if (!progressCard || !progressBar || !progressValue || !progressLabel) {
		return;
	}

	progressCard.hidden = false;
	progressCard.classList.toggle('is-processing', Boolean(isProcessing));
	progressBar.style.width = `${Math.max(0, Math.min(percentage, 100))}%`;
	progressValue.textContent = `${Math.round(Math.max(0, Math.min(percentage, 100)))}%`;
	progressLabel.textContent = label;
}

function setSelectedFile(file) {
	const dropzone = document.getElementById('dropzone');
	const selectedFile = document.getElementById('selectedFile');
	const selectedFileName = document.getElementById('selectedFileName');
	const selectedFileMeta = document.getElementById('selectedFileMeta');

	if (!dropzone || !selectedFile || !selectedFileName || !selectedFileMeta) {
		return;
	}

	if (!file) {
		dropzone.classList.remove('has-file');
		selectedFile.hidden = true;
		selectedFileName.textContent = 'No file selected';
		selectedFileMeta.textContent = '';
		return;
	}

	dropzone.classList.add('has-file');
	selectedFile.hidden = false;
	selectedFileName.textContent = file.name;
	selectedFileMeta.textContent = `${formatBytes(file.size)} · ${file.type || 'image file'}`;
}

function initializeUploadExperience() {
	const uploadForm = document.getElementById('uploadForm');
	const fileInput = document.getElementById('fileInput');
	const dropzone = document.getElementById('dropzone');

	if (!uploadForm || !fileInput || !dropzone) {
		return;
	}

	const browseFromKeyboard = (event) => {
		if (event.key !== 'Enter' && event.key !== ' ') {
			return;
		}

		event.preventDefault();
		fileInput.click();
	};

	const assignFiles = (files) => {
		if (!files || files.length === 0) {
			return;
		}

		const [file] = files;

		if (typeof DataTransfer !== 'undefined') {
			// Rebuild a FileList so drag-drop behaves like a native file input selection.
			const dataTransfer = new DataTransfer();
			dataTransfer.items.add(file);
			fileInput.files = dataTransfer.files;
		}

		setSelectedFile(file);
	};

	['dragenter', 'dragover'].forEach((eventName) => {
		dropzone.addEventListener(eventName, (event) => {
			event.preventDefault();
			dropzone.classList.add('is-dragover');
		});
	});

	['dragleave', 'dragend', 'drop'].forEach((eventName) => {
		dropzone.addEventListener(eventName, () => {
			dropzone.classList.remove('is-dragover');
		});
	});

	dropzone.addEventListener('drop', (event) => {
		event.preventDefault();
		assignFiles(event.dataTransfer?.files);
	});

	dropzone.addEventListener('keydown', browseFromKeyboard);

	fileInput.addEventListener('change', () => {
		setSelectedFile(fileInput.files?.[0] ?? null);
	});

	uploadForm.addEventListener('submit', (event) => {
		event.preventDefault();

		const file = fileInput.files?.[0];
		if (!file) {
			fileInput.click();
			return;
		}

		// XHR is used instead of fetch because it exposes granular upload progress events.
			if (!progressEvent.lengthComputable) {
				updateUploadProgress(55, 'Uploading image...', false);
				return;
			}

			const percentage = Math.min((progressEvent.loaded / progressEvent.total) * 85, 85);
			updateUploadProgress(percentage, 'Uploading image...', false);
		});

		request.upload.addEventListener('load', () => {
			updateUploadProgress(92, 'Upload complete. Processing image variants...', true);
		});

		request.addEventListener('load', () => {
			updateUploadProgress(100, 'Finishing response...', false);
			// Replace the current document with the server-rendered response to keep TempData and antiforgery state in sync.
			document.open();
			document.write(request.responseText);
			document.close();
		});

		request.addEventListener('error', () => {
			setUploadBusyState(false);
			updateUploadProgress(0, 'Upload failed. Please try again.', false);
		});

		request.send(formData);
	});
}

window.showUploadSpinner = function () {
	setUploadBusyState(true);
};

window.copyShareLink = function (event) {
	event.preventDefault();

	const button = event.currentTarget;
	const relativeUrl = button?.dataset?.url;
	if (!relativeUrl) {
		return;
	}

	const link = new URL(relativeUrl, window.location.origin).href;
	navigator.clipboard.writeText(link).then(() => {
		const label = button.querySelector('span');
		const originalText = label ? label.textContent : button.textContent;

		if (label) {
			label.textContent = 'Copied';
		} else {
			button.textContent = 'Copied';
		}

		setTimeout(() => {
			if (label) {
				label.textContent = originalText;
			} else {
				button.textContent = originalText;
			}
		}, 1800);
	}).catch(() => {
		alert('Failed to copy link. Please try again or copy manually.');
	});
};

window.deleteFile = function (event, fileId) {
	event.preventDefault();

	if (!confirm('Are you sure you want to delete this file? Access will be revoked for all shares.')) {
		return;
	}

	const button = event.currentTarget;
	const label = button.querySelector('span');
	const originalText = label ? label.textContent : button.textContent;

	button.disabled = true;
	if (label) {
		label.textContent = 'Deleting...';
	} else {
		button.textContent = 'Deleting...';
	}

	const form = document.createElement('form');
	form.method = 'POST';
	form.action = `/delete/${fileId}`;

	const token = document.querySelector('input[name="__RequestVerificationToken"]');
	if (token) {
		// Clone the existing antiforgery token so delete POSTs pass server validation.
		form.appendChild(token.cloneNode(true));
	}

	document.body.appendChild(form);
	form.submit();

	window.setTimeout(() => {
		button.disabled = false;
		if (label) {
			label.textContent = originalText;
		} else {
			button.textContent = originalText;
		}
	}, 2500);
};

document.addEventListener('DOMContentLoaded', initializeUploadExperience);
