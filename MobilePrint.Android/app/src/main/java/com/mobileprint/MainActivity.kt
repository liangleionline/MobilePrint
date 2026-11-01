package com.mobileprint

import android.content.Intent
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

    private lateinit var etServerIp: EditText
    private lateinit var etToken: EditText
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

    private var selectedFileUri: Uri? = null
    private var fileName: String = ""
    private val client = OkHttpClient.Builder()
        .connectTimeout(10, TimeUnit.SECONDS)
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

        etServerIp = findViewById(R.id.etServerIp)
        etToken = findViewById(R.id.etToken)
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

        etServerIp.setText(android.content.SharedPreferences::class.let {
            getSharedPreferences("mp", MODE_PRIVATE).getString("ip", "192.168.1.100")
        })
        etToken.setText(getSharedPreferences("mp", MODE_PRIVATE).getString("token", "123456"))

        btnSelectFile.setOnClickListener { filePicker.launch("*/*") }
        btnPrint.setOnClickListener { doPrint() }

        // populate spinners
        val orientationAdapter = ArrayAdapter.createFromResource(this, R.array.orientation_items, android.R.layout.simple_spinner_item)
        orientationAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        spOrientation.adapter = orientationAdapter

        val scaleAdapter = ArrayAdapter.createFromResource(this, R.array.scale_items, android.R.layout.simple_spinner_item)
        scaleAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
        spScaleMode.adapter = scaleAdapter
    }

    private fun queryName(uri: Uri): String? {
        contentResolver.query(uri, null, null, null, null)?.use { c ->
            val idx = c.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
            if (idx >= 0 && c.moveToFirst()) return c.getString(idx)
        }
        return null
    }

    private fun doPrint() {
        if (selectedFileUri == null) { toast("请先选择文件"); return }
        val ip = etServerIp.text.toString().trim()
        val token = etToken.text.toString().trim()
        if (ip.isEmpty()) { toast("请输入服务器IP"); return }

        getSharedPreferences("mp", MODE_PRIVATE).edit()
            .putString("ip", ip).putString("token", token).apply()

        tvStatus.text = "上传中..."
        btnPrint.isEnabled = false

        lifecycleScope.launch {
            try {
                val result = withContext(Dispatchers.IO) {
                    uploadFile(ip, token)
                }
                tvStatus.text = result
                toast(result)
            } catch (e: Exception) {
                tvStatus.text = "失败: ${e.message}"
                toast("失败: ${e.message}")
            } finally {
                btnPrint.isEnabled = true
            }
        }
    }

    private fun uploadFile(ip: String, token: String): String {
        val ext = fileName.substringAfterLast('.', "").lowercase()
        var bytes = contentResolver.openInputStream(selectedFileUri!!)!!.use { it.readBytes() }

        // compress images
        if (swCompress.isChecked && ext in listOf("jpg", "jpeg", "png", "bmp", "webp")) {
            bytes = compressImage(bytes, 1920, 75)
        }

        val baseUrl = "http://$ip:8899"
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
