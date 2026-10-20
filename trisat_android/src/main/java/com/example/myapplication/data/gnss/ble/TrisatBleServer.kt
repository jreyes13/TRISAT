package com.example.myapplication.data.gnss.ble

import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothGattServer
import android.bluetooth.BluetoothGattServerCallback
import android.bluetooth.BluetoothGattService
import android.bluetooth.BluetoothManager
import android.bluetooth.le.AdvertiseCallback
import android.bluetooth.le.AdvertiseData
import android.bluetooth.le.AdvertiseSettings
import android.bluetooth.le.BluetoothLeAdvertiser
import android.content.Context
import android.os.ParcelUuid
import android.util.Log
import com.example.myapplication.data.gnss.model.GalileoInavMessage
import com.example.myapplication.data.gnss.model.LocationData
import com.example.myapplication.data.gnss.model.SatelliteDetail
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

class TrisatBleServer(private val context: Context) {

    companion object {
        const val TAG = "TrisatBleServer"
        val SERVICE_UUID: UUID = UUID.fromString("00001835-0000-1000-8000-00805F9B34FB")
        val INAV_CHAR_UUID: UUID = UUID.fromString("00002A90-0000-1000-8000-00805F9B34FB")
        val SAT_STATUS_CHAR_UUID: UUID = UUID.fromString("00002A91-0000-1000-8000-00805F9B34FB")
        val LOCATION_CHAR_UUID: UUID = UUID.fromString("00002A92-0000-1000-8000-00805F9B34FB")
        val CCCD_UUID: UUID = UUID.fromString("00002902-0000-1000-8000-00805F9B34FB")
    }

    private var bluetoothManager: BluetoothManager? = context.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
    private var bluetoothAdapter: BluetoothAdapter? = bluetoothManager?.adapter
    private var bluetoothLeAdvertiser: BluetoothLeAdvertiser? = null
    private var gattServer: BluetoothGattServer? = null

    private var inavChar: BluetoothGattCharacteristic? = null
    private var satStatusChar: BluetoothGattCharacteristic? = null
    private var locationChar: BluetoothGattCharacteristic? = null

    var isServerActive: Boolean = false
        private set

    val deviceName: String
        get() {
            return try {
                bluetoothAdapter?.name ?: "TRISAT Android Sensor Pod"
            } catch (_: SecurityException) {
                "TRISAT Android Sensor Pod"
            }
        }

    fun startServer(): Boolean {
        if (bluetoothAdapter == null || !bluetoothAdapter!!.isEnabled) {
            Log.e(TAG, "Bluetooth disabled or unavailable.")
            return false
        }

        bluetoothLeAdvertiser = bluetoothAdapter?.bluetoothLeAdvertiser
        if (bluetoothLeAdvertiser == null) {
            Log.e(TAG, "BLE Advertiser unsupported.")
            return false
        }

        setupGattServer()
        startAdvertising()
        isServerActive = true
        Log.i(TAG, "TRISAT BLE Bridge Server initialized as: $deviceName")
        return true
    }

    fun stopServer() {
        try {
            bluetoothLeAdvertiser?.stopAdvertising(advertiseCallback)
            gattServer?.close()
            gattServer = null
            isServerActive = false
            Log.i(TAG, "TRISAT BLE Bridge Server stopped.")
        } catch (e: SecurityException) {
            Log.e(TAG, "Security exception stopping BLE server: ${e.message}")
        }
    }

    private fun setupGattServer() {
        try {
            gattServer = bluetoothManager?.openGattServer(context, gattServerCallback)

            val service = BluetoothGattService(SERVICE_UUID, BluetoothGattService.SERVICE_TYPE_PRIMARY)

            inavChar = BluetoothGattCharacteristic(
                INAV_CHAR_UUID,
                BluetoothGattCharacteristic.PROPERTY_READ or BluetoothGattCharacteristic.PROPERTY_NOTIFY,
                BluetoothGattCharacteristic.PERMISSION_READ
            ).apply {
                addDescriptor(BluetoothGattDescriptor(CCCD_UUID, BluetoothGattDescriptor.PERMISSION_READ or BluetoothGattDescriptor.PERMISSION_WRITE))
            }

            satStatusChar = BluetoothGattCharacteristic(
                SAT_STATUS_CHAR_UUID,
                BluetoothGattCharacteristic.PROPERTY_READ or BluetoothGattCharacteristic.PROPERTY_NOTIFY,
                BluetoothGattCharacteristic.PERMISSION_READ
            ).apply {
                addDescriptor(BluetoothGattDescriptor(CCCD_UUID, BluetoothGattDescriptor.PERMISSION_READ or BluetoothGattDescriptor.PERMISSION_WRITE))
            }

            locationChar = BluetoothGattCharacteristic(
                LOCATION_CHAR_UUID,
                BluetoothGattCharacteristic.PROPERTY_READ or BluetoothGattCharacteristic.PROPERTY_NOTIFY,
                BluetoothGattCharacteristic.PERMISSION_READ
            ).apply {
                addDescriptor(BluetoothGattDescriptor(CCCD_UUID, BluetoothGattDescriptor.PERMISSION_READ or BluetoothGattDescriptor.PERMISSION_WRITE))
            }

            service.addCharacteristic(inavChar)
            service.addCharacteristic(satStatusChar)
            service.addCharacteristic(locationChar)

            gattServer?.addService(service)
        } catch (e: SecurityException) {
            Log.e(TAG, "GATT Server security exception: ${e.message}")
        }
    }

    private fun startAdvertising() {
        try {
            if (bluetoothAdapter?.name.isNullOrEmpty() || !bluetoothAdapter?.name!!.contains("TRISAT")) {
                bluetoothAdapter?.name = "TRISAT Android Sensor Pod"
            }
        } catch (_: SecurityException) {}

        val settings = AdvertiseSettings.Builder()
            .setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_LOW_LATENCY)
            .setConnectable(true)
            .setTimeout(0)
            .setTxPowerLevel(AdvertiseSettings.ADVERTISE_TX_POWER_HIGH)
            .build()

        val data = AdvertiseData.Builder()
            .setIncludeDeviceName(true)
            .addServiceUuid(ParcelUuid(SERVICE_UUID))
            .build()

        try {
            bluetoothLeAdvertiser?.startAdvertising(settings, data, advertiseCallback)
        } catch (e: SecurityException) {
            Log.e(TAG, "BLE advertising security exception: ${e.message}")
        }
    }

    fun broadcastInavFrame(msg: GalileoInavMessage) {
        if (!isServerActive) return
        val json = JSONObject().apply {
            put("svid", msg.svid)
            put("submsgId", msg.submessageId)
            put("hex", msg.hexData)
            put("ts", msg.timestampMillis)
        }.toString()

        @Suppress("DEPRECATION")
        inavChar?.value = json.toByteArray(Charsets.UTF_8)
        notifyConnectedDevices(inavChar)
    }

    fun broadcastSatStatus(satellites: List<SatelliteDetail>) {
        if (!isServerActive) return
        val array = JSONArray()
        satellites.forEach { sat ->
            val obj = JSONObject().apply {
                put("svid", sat.svid)
                put("const", sat.constellation.name)
                put("elev", sat.elevationDegrees)
                put("azim", sat.azimuthDegrees)
                put("cno", sat.cn0DbHz)
                put("used", sat.usedInFix)
            }
            array.put(obj)
        }

        @Suppress("DEPRECATION")
        satStatusChar?.value = array.toString().toByteArray(Charsets.UTF_8)
        notifyConnectedDevices(satStatusChar)
    }

    fun broadcastLocation(location: LocationData) {
        if (!isServerActive) return
        val json = JSONObject().apply {
            put("lat", location.latitude)
            put("lon", location.longitude)
            put("alt", location.altitude)
            put("acc", location.accuracy)
        }.toString()

        @Suppress("DEPRECATION")
        locationChar?.value = json.toByteArray(Charsets.UTF_8)
        notifyConnectedDevices(locationChar)
    }

    private fun notifyConnectedDevices(characteristic: BluetoothGattCharacteristic?) {
        if (characteristic == null || gattServer == null) return
        try {
            bluetoothManager?.getConnectedDevices(BluetoothGattServer.GATT)?.forEach { device ->
                @Suppress("DEPRECATION")
                gattServer?.notifyCharacteristicChanged(device, characteristic, false)
            }
        } catch (e: SecurityException) {
            Log.e(TAG, "Notification security exception: ${e.message}")
        }
    }

    private val advertiseCallback = object : AdvertiseCallback() {
        override fun onStartSuccess(settingsInEffect: AdvertiseSettings?) {
            Log.i(TAG, "BLE Advertising started successfully.")
        }

        override fun onStartFailure(errorCode: Int) {
            Log.e(TAG, "BLE Advertising failed with error code: $errorCode")
        }
    }

    private val gattServerCallback = object : BluetoothGattServerCallback() {
        override fun onConnectionStateChange(device: BluetoothDevice?, status: Int, newState: Int) {
            val stateStr = if (newState == BluetoothGatt.STATE_CONNECTED) "CONNECTED" else "DISCONNECTED"
            Log.i(TAG, "BLE Client ${device?.address} state changed: $stateStr")
        }

        override fun onDescriptorWriteRequest(
            device: BluetoothDevice?,
            requestId: Int,
            descriptor: BluetoothGattDescriptor?,
            preparedWrite: Boolean,
            responseNeeded: Boolean,
            offset: Int,
            value: ByteArray?
        ) {
            try {
                if (responseNeeded && device != null) {
                    gattServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, value)
                }
                Log.i(TAG, "GATT CCCD subscription write accepted for ${device?.address}")
            } catch (e: SecurityException) {
                Log.e(TAG, "Security exception on sendResponse: ${e.message}")
            }
        }

        override fun onCharacteristicReadRequest(
            device: BluetoothDevice?,
            requestId: Int,
            offset: Int,
            characteristic: BluetoothGattCharacteristic?
        ) {
            try {
                if (device != null) {
                    @Suppress("DEPRECATION")
                    gattServer?.sendResponse(device, requestId, BluetoothGatt.GATT_SUCCESS, offset, characteristic?.value)
                }
            } catch (e: SecurityException) {
                Log.e(TAG, "Security exception on characteristic read response: ${e.message}")
            }
        }
    }
}
