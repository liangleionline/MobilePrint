package com.mobileprint

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Bundle
import android.widget.*
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.util.concurrent.TimeUnit

class MainActivity : AppCompatActivity() {

    private lateinit var etServerAddr: EditText
    private lateinit var etToken: EditText
    private lateinit var btnTestConn: Button
    private lateinit var tvConnStatus: TextView
    private lateinit var btnSelectFile: Button
    private lateinit var tvFileName: TextView
    private lateinit var etCopies: EditText
    private lateinit var spOrientation: Spinner
    private lateinit var etPageRange: EditText
    private lateinit var etMargin: EditText
    private lateinit var spScaleMode: Spinner
    private lateinit var swCompress: Switch
    private lateinit var btnPrint: Button
    private lateinit var tvStatus: TextView

    private var connected = false
    private var baseUrl = ""
    private var selectedFileUri: Uri? = null
    private var fileName: String = ""

    private val client = OkHttpClient.Builder()
        .connectTimeout(8, TimeUnit.SECONDS)
        .writeTimeout(120, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .build()

    private val filePicker = registerForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        uri?.let {
            selectedFileUri = it
            fileName = queryName(it) ?: "selected_file"
            tvFileName.text = "已选: $fileName"
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        etServerAddr = findViewById(R.id.etServerAddr)
        etToken = findViewById(R.id.etToken)
        btnTestConn = findViewById(R.id.btnTestConn)
        tvConnStatus = findViewById(R.id.tvConnStatus)
        btnSelectFile = findViewById(R.id.btnSelectFile)
        tvFileName = findViewById(R.id.tvFileName)
        etCopies = findViewById(R.id.etCopies)
        spOrientation = findViewById(R.id.spOrientation)
        etPageRange = findViewById(R.id.etPageRange)
        etMargin = findViewById(R.id.etMargin)
        spScaleMode = findViewById(R.id.spScaleMode)
        swCompress = findViewById(R.id.swCompress)
        btnPrint = findViewById(R.id.btnPrint)
        tvStatus = findViewById(R.id.tvStatus)

        etServerAddr.setText(getSharedPreferences("mp", MODE_PRIVATE).getString("addr", "192.168.0.197:5003"))
        etToken.setText(getSharedPreferences("mp", MODE_PRIVATE).getString("token", "123456"))

        btnTestConn.setOnClickListener { testConnection() }
        btnSelectFile.setOnClickListener { filePicker.launch("*/*") }
        btnPrint.setOnClickListener { doPrint() }

        val orientationAdapter = ArrayAdapter.createFromResource(this, R.array.orientation_items, android.R.layout.simple_spinner_item)
        orientationAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        spOrientation.adapter = orientationAdapter

        val scaleAdapter = ArrayAdapter.createFromResource(this, R.array.scale_items, android.R.layout.simple_spinner_item)
        scaleAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        spScaleMode.adapter = scaleAdapter
    }

    private fun setConnected(ok: Boolean) {
        connected = ok
        btnSelectFile.isEnabled = ok
        btnPrint.isEnabled = ok
        tvConnStatus.text = if (ok) "已连接 ✓" else "未连接"
        tvConnStatus.setTextColor(if (ok) 0xFF4CAF50.toInt() else 0xFF999999.toInt())
    }

    private fun testConnection() {
        val addr = etServerAddr.text.toString().trim()
        val token = etToken.text.toString().trim()
        if (addr.isEmpty()) { toast("请输入服务器地址"); return }

        baseUrl = "http://$addr"
        btnTestConn.isEnabled = false
        btnTestConn.text = "测试中..."
        tvConnStatus.text = "测试中..."

        lifecycleScope.launch {
            try {
                val result = withContext(Dispatchers.IO) {
                    val req = Request.Builder()
                        .url("$baseUrl/api/heartbeat")
                        .header("Authorization", "Bearer $token")
                        .get()
                        .build()
                    client.newCall(req).execute().use { resp ->
                        val body = resp.body?.string() ?: ""
                        if (resp.isSuccessful) {
                            try {
                                val json = JSONObject(body)
                                val data = json.optJSONObject("data")
                                val port = data?.optInt("port") ?: -1
                                val printer = data?.optString("printer") ?: "?"
                                Triple(true, "端口:$port 打印机:$printer", "")
                            } catch (e: Exception) {
                                Triple(true, "响应: ${body.take(100)}", "")
                            }
                        } else {
                            Triple(false, "HTTP ${resp.code}", body.take(200))
                        }
                    }
                }
                if (result.first) {
                    setConnected(true)
                    toast("连接成功!")
                    getSharedPreferences("mp", MODE_PRIVATE).edit()
                        .putString("addr", addr).putString("token", token).apply()
                } else {
                    setConnected(false)
                    tvConnStatus.text = "失败: ${result.second}"
                    tvConnStatus.setTextColor(0xFFF44336.toInt())
                    toast("连接失败: ${result.second}")
                }
            } catch (e: Exception) {
                setConnected(false)
                val msg = e.message ?: "未知错误"
                tvConnStatus.text = "失败: $msg"
                tvConnStatus.setTextColor(0xFFF44336.toInt())
                toast("连接失败: $msg")
            } finally {
                btnTestConn.isEnabled = true
                btnTestConn.text = "测试连接"
            }
        }
    }

    private fun queryName(uri: Uri): String? {
        contentResolver.query(uri, null, null, null, null)?.use { c ->
            val idx = c.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
            if (idx >= 0 && c.moveToFirst()) return c.getString(idx)
        }
        return null
    }

    private fun doPrint() {
        if (!connected) { toast("请先测试连接"); return }
        if (selectedFileUri == null) { toast("请先选择文件"); return }
        val token = etToken.text.toString().trim()

        tvStatus.text = "上传中..."
        btnPrint.isEnabled = false

        lifecycleScope.launch {
            try {
                val result = withContext(Dispatchers.IO) { uploadFile(token) }
                tvStatus.text = result
                toast(result)
            } catch (e: Exception) {
                val msg = "失败: ${e.message}"
                tvStatus.text = msg
                toast(msg)
            } finally {
                btnPrint.isEnabled = true
            }
        }
    }

    private fun uploadFile(token: String): String {
        val ext = fileName.substringAfterLast('.', "").lowercase()
        var bytes = contentResolver.openInputStream(selectedFileUri!!)!!.use { it.readBytes() }

        if (swCompress.isChecked && ext in listOf("jpg", "jpeg", "png", "bmp", "webp")) {
            bytes = compressImage(bytes, 1920, 75)
        }

        val body = MultipartBody.Builder().setType(MultipartBody.FORM)
            .addFormDataPart("file", fileName, bytes.toRequestBody("application/octet-stream".toMediaType()))
            .addFormDataPart("copies", etCopies.text.toString().ifEmpty { "1" })
            .addFormDataPart("orientation", if (spOrientation.selectedItemPosition == 1) "Landscape" else "Portrait")
            .addFormDataPart("pageRange", etPageRange.text.toString())
            .addFormDataPart("scaleMode", when (spScaleMode.selectedItemPosition) { 1 -> "Original"; 2 -> "Custom"; else -> "FitA4" })
            .addFormDataPart("marginTop", etMargin.text.toString().ifEmpty { "10" })
            .addFormDataPart("marginBottom", etMargin.text.toString().ifEmpty { "10" })
            .addFormDataPart("marginLeft", etMargin.text.toString().ifEmpty { "10" })
            .addFormDataPart("marginRight", etMargin.text.toString().ifEmpty { "10" })
            .build()

        val req = Request.Builder()
            .url("$baseUrl/api/print/upload")
            .header("Authorization", "Bearer $token")
            .post(body)
            .build()

        client.newCall(req).execute().use { resp ->
            val respBody = resp.body?.string() ?: ""
            val json = JSONObject(respBody)
            if (json.optInt("code") == 200) {
                val data = json.getJSONObject("data")
                return "上传成功! 任务ID: ${data.optString("taskId")}, 共${data.optInt("totalPage")}页"
            } else {
                throw Exception(json.optString("msg", "未知错误"))
            }
        }
    }

    private fun compressImage(raw: ByteArray, maxLongSide: Int, quality: Int): ByteArray {
        val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(raw, 0, raw.size, opts)
        var w = opts.outWidth
        var h = opts.outHeight
        var sample = 1
        while (w / sample > maxLongSide || h / sample > maxLongSide) sample *= 2
        opts.inJustDecodeBounds = false
        opts.inSampleSize = sample
        val bmp = BitmapFactory.decodeByteArray(raw, 0, raw.size, opts) ?: return raw
        val out = ByteArrayOutputStream()
        bmp.compress(Bitmap.CompressFormat.JPEG, quality, out)
        bmp.recycle()
        return out.toByteArray()
    }

    private fun toast(msg: String) = Toast.makeText(this, msg, Toast.LENGTH_LONG).show()
}
