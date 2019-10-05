package com.ackdev.childDev.service;

import java.io.IOException;
import java.util.Collection;
import java.util.logging.Logger;

import javax.servlet.http.HttpServletRequest;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.web.bind.annotation.CrossOrigin;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.ackdev.childDev.bean.JournalBean;
import com.ackdev.childDev.jdo.JournalItem;
import com.ackdev.common.dto.CommonResponse;
import com.ackdev.common.dto.CommonResponse.Status;

import io.swagger.annotations.ApiOperation;
import io.swagger.annotations.ApiParam;
import io.swagger.annotations.ApiResponse;
import io.swagger.annotations.ApiResponses;
import io.swagger.annotations.Authorization;
@SpringBootApplication
@RestController
@CrossOrigin(origins = "*")
@RequestMapping("/rest/secure/journal")
public class JournalService {
	

	@Autowired
	private JournalBean jb;
	
	private static final Logger LOG = Logger.getLogger(JournalService.class.getName());

	@DeleteMapping("/{guid}")
	@ApiOperation(value = "Soft delete a journal entry", notes = "Expiration date is set to time of deletion", response = CommonResponse.Status.class, authorizations = @Authorization(value = "api_key"))
	@ApiResponses(value = { @ApiResponse(code = 404, message = "Invalid ID supplied"),
			@ApiResponse(code = 200, message = "success"), @ApiResponse(code = 401, message = "unauthorized") })
	public CommonResponse.Status delete(@ApiParam(value = "ID of property to return") @PathVariable String guid) throws IOException {
		// UserSession sUserSession = ThreadInstance.getUserSession();

		return null;
	}

	@DeleteMapping("/hardDelete/{guid}")
	@ApiOperation(value = "Hard delete a journal entry", notes = "Expiration date is set to time of deletion", response = CommonResponse.Status.class, authorizations = @Authorization(value = "api_key"))
	@ApiResponses(value = { @ApiResponse(code = 404, message = "Invalid ID supplied"),
			@ApiResponse(code = 200, message = "success"), @ApiResponse(code = 401, message = "unauthorized") })
	public CommonResponse.Status hardDelete(@PathVariable String guid) {
		return jb.delete(guid);
	}

	@GetMapping("/range/{startTS}/{endTS}")
	public Collection<JournalItem> getRange(@PathVariable Long startTS, @PathVariable Long endTS,
			HttpServletRequest request) {
		return jb.getRange(startTS, endTS);
	}

	@GetMapping("/{guid}")
	public JournalItem get(@PathVariable String guid) {
		return jb.get(guid);
	}

	@PostMapping(consumes = MediaType.APPLICATION_JSON_VALUE, produces = MediaType.APPLICATION_JSON_VALUE)
	public CommonResponse.Status saveJournalEntry(@RequestBody JournalItem journalItem,
			@RequestHeader HttpHeaders headers) {
		//jb.UT_InjectUserStore(this.requestUserStore);
		Status ret = jb.update(journalItem);
		//LOG.info("saveJournalEntry "+ret.isSuccess());
		return ret;
	}

	@PostMapping(path = "/batch", consumes = MediaType.APPLICATION_JSON_UTF8_VALUE, produces = MediaType.APPLICATION_JSON_UTF8_VALUE)
	public Collection<CommonResponse.Status> saveBatchJournalEntry(
			@RequestBody Collection<JournalItem> journalCollection, HttpServletRequest request) {
		return jb.update(journalCollection);
	}

}
