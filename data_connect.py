
from fastapi import APIRouter, Depends
from fastapi import FastAPI, File, UploadFile
import requests
import json
from fastapi import APIRouter, Depends
from fastapi import FastAPI, File, UploadFile, HTTPException
from io import BytesIO

import uvicorn

app = FastAPI()
from fastapi.responses import JSONResponse


def upload_file(file_path, user_id="abc-123"):
    # 上传文件的 URL

    # API的URL
    url = "http://10.2.90.20/v1/files/upload"
    # 将UploadFile转换为bytes
    file_bytes = file_path.file.read()
    # 使用BytesIO模拟文件对象
    file_like = BytesIO(file_bytes)

    # 设置Authorization头部
    headers = {"Authorization": "Bearer app-TO71soyj4gqcRsG4hH37lThc"}  # 替换为你的实际API密钥

    # 要上传的文件路径
    # file_path = 'localfile'  # 替换为你的文件路径

    # 打开文件并将其附加到表单数据
    files = {
        "file": (file_path.filename, file_like, "document/xls|xlsx"),
    }

    # 其他表单数据
    data = {"user": "abc-123"}

    # 发送POST请求
    response = requests.post(url, headers=headers, files=files, data=data)

    if response.status_code == 201:
        return response.json()["id"]  # 假设返回的 JSON 中包含 "id"
    else:
        return f"Error: {response.status_code}, {response.text}"


def send_chat_message(api_key, upload_file_id):
    url = "http://10.2.90.20/v1/chat-messages"

    headers = {"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"}

    data = {
        "inputs": {},
        "query": "执行excel钻库数据提取",
        "response_mode": "blocking",
        "conversation_id": "",
        "user": "abc-123",
        "files": [{"type": "document", "transfer_method": "local_file", "upload_file_id": upload_file_id}],
    }

    # Convert data to JSON format
    payload = json.dumps(data)

    # Send POST request
    response = requests.post(url, headers=headers, data=payload)

    if response.status_code == 200:
        #print(response.text)
        data = response.json()
 
        
        return data["answer"]
    else:
        return f"Error: {response.status_code}, {response.text}"


router = APIRouter(prefix="/connect", tags=["connect"])


@app.post("/send_chat_message_endpoint/")
async def send_chat_message_endpoint(file: UploadFile = File(...)):
    try:
        # 调用发送聊天消息逻辑
        url = "http://10.2.90.20/v1/files/upload"
        # 将UploadFile转换为bytes
        file_bytes = await file.read()
        # 使用BytesIO模拟文件对象
        file_like = BytesIO(file_bytes)

        # 设置Authorization头部
        headers = {"Authorization": "Bearer app-TO71soyj4gqcRsG4hH37lThc"}  # 替换为你的实际API密钥

        # 要上传的文件路径
        # file_path = 'localfile'  # 替换为你的文件路径

        # 打开文件并将其附加到表单数据
        files = {
            "file": (file.filename, file_like, "document/xls|xlsx"),
        }

        # 其他表单数据
        data = {"user": "abc-123"}

        # 发送POST请求
        response = requests.post(url, headers=headers, files=files, data=data)

        if response.status_code == 201:
            a= response.json()["id"]  # 假设返回的 JSON 中包含 "id"
        #upload_file_id = upload_file(file)
        api_key = "app-TO71soyj4gqcRsG4hH37lThc"
        output = send_chat_message(api_key, a)
        return {"result": output}
    except HTTPException as e:
        raise e





if __name__ == "__main__":
    # 使用 uvicorn.run 启动应用，host='0.0.0.0' 让应用可外部访问，port=8000 设置端口
    uvicorn.run(app, host="0.0.0.0", port=8010)
