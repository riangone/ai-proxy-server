const chat = document.getElementById('chat');
const promptInput = document.getElementById('prompt');
const fileInput = document.getElementById('fileInput');
const sendBtn = document.getElementById('sendBtn');
const status = document.getElementById('status');

function addMessage(text, role) {
  const div = document.createElement('div');
  div.className = `message ${role}`;
  div.textContent = text;
  chat.appendChild(div);
  chat.scrollTop = chat.scrollHeight;
}

sendBtn.addEventListener('click', async () => {
  const prompt = promptInput.value;
  const file = fileInput.files[0];
  
  if (!file && !prompt) {
    alert("Please provide a file or a prompt.");
    return;
  }

  status.textContent = "Processing...";
  addMessage(prompt || "Parsing file...", "user");

  const formData = new FormData();
  if (file) formData.append('file', file);
  formData.append('instruction', prompt);
  formData.append('schemaType', 'uriage');

  try {
    const response = await fetch('http://localhost:5001/api/parse-invoice', {
      method: 'POST',
      body: formData
    });

    if (!response.ok) throw new Error("Server error");

    const data = await response.json();
    addMessage("AI parsed the data. Filling the form...", "ai");
    
    // Send data to content script to fill the form
    chrome.runtime.sendMessage({
      type: "FILL_FORM",
      data: data
    });

    status.textContent = "Success!";
  } catch (err) {
    console.error(err);
    addMessage("Error: " + err.message, "ai");
    status.textContent = "Error occurred.";
  }
});
